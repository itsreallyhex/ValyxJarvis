// Valyx Jarvis desktop app: hearing and speaking. Mic (or a WAV file in tests) -> wake word and offline commands
// (System.Speech, runs on this PC) -> an utterance WAV for speech-to-text. And text-to-speech plus small chimes.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;

namespace ValyxJarvis
{
    /// <summary>16 kHz, mono, 16-bit PCM: what every recognizer here wants.</summary>
    public static class Wav
    {
        public const int Rate = 16000, BytesPerMs = 32;

        public static byte[] Wrap(byte[] pcm)
        {
            var ms = new MemoryStream(44 + pcm.Length);
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + pcm.Length); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(pcm.Length); w.Write(pcm);
            return ms.ToArray();
        }

        /// <summary>The PCM of a 16 kHz mono 16-bit WAV (anything else is refused: tests must say what they feed).</summary>
        public static byte[] Pcm(byte[] wav)
        {
            if (wav.Length < 44 || wav[0] != 'R' || wav[8] != 'W') throw new InvalidDataException("not a WAV");
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
                int len = BitConverter.ToInt32(wav, pos + 4);
                if (id == "fmt ")
                {
                    if (BitConverter.ToInt16(wav, pos + 8) != 1 || BitConverter.ToInt16(wav, pos + 10) != 1 || BitConverter.ToInt32(wav, pos + 12) != Rate || BitConverter.ToInt16(wav, pos + 22) != 16)
                        throw new InvalidDataException("WAV must be 16 kHz mono 16-bit");
                }
                else if (id == "data")
                {
                    len = Math.Min(len, wav.Length - pos - 8);
                    var pcm = new byte[len]; Buffer.BlockCopy(wav, pos + 8, pcm, 0, len); return pcm;
                }
                pos += 8 + len + (len & 1);
            }
            throw new InvalidDataException("no data chunk");
        }

        /// <summary>A 16-bit PCM WAV at another volume (0..1), its RIFF and data sizes made right. Anything else comes back unchanged.</summary>
        public static byte[] Scale(byte[] wav, double volume)
        {
            try
            {
                if (wav.Length < 44 || wav[0] != 'R' || wav[8] != 'W') return wav;
                int pos = 12, bits = 0, format = 0;
                while (pos + 8 <= wav.Length)
                {
                    var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
                    long len = BitConverter.ToUInt32(wav, pos + 4);
                    if (id == "fmt ") { format = BitConverter.ToInt16(wav, pos + 8); bits = BitConverter.ToInt16(wav, pos + 22); }
                    else if (id == "data")
                    {
                        if (format != 1 || bits != 16) return wav;
                        int start = pos + 8, n = (int)Math.Min(len, wav.Length - start) & ~1;
                        var o = new byte[start + n]; Buffer.BlockCopy(wav, 0, o, 0, start + n);
                        for (int i = start; i < start + n; i += 2)
                        {
                            int v = (int)(BitConverter.ToInt16(o, i) * volume);
                            v = Math.Max(short.MinValue, Math.Min(short.MaxValue, v));
                            o[i] = (byte)(v & 0xff); o[i + 1] = (byte)((v >> 8) & 0xff);
                        }
                        Array.Copy(BitConverter.GetBytes(o.Length - 8), 0, o, 4, 4);
                        Array.Copy(BitConverter.GetBytes(n), 0, o, pos + 4, 4);
                        return o;
                    }
                    pos += 8 + (int)Math.Min(len, int.MaxValue / 2) + (int)(len & 1);
                }
            }
            catch { }
            return wav;
        }

        /// <summary>The speech part of a 16 kHz recording: leading and trailing quiet cut, 250 ms kept around it (Whisper invents words in silence).
        /// Returns the input when nothing stands out from the noise.</summary>
        public static byte[] TrimSilence(byte[] wav)
        {
            byte[] pcm; try { pcm = Pcm(wav); } catch { return wav; }
            const int step = 640;   // 20 ms
            int n = pcm.Length / step; if (n < 5) return wav;
            var levels = new double[n];
            for (int i = 0; i < n; i++) { var seg = new byte[step]; Buffer.BlockCopy(pcm, i * step, seg, 0, step); levels[i] = Rms(seg, step); }
            var sorted = (double[])levels.Clone(); Array.Sort(sorted);
            double floor = sorted[n / 5], gate = Math.Max(400, floor * 3);   // the quiet fifth is the room
            int first = Array.FindIndex(levels, v => v > gate), last = Array.FindLastIndex(levels, v => v > gate);
            if (first < 0) return wav;
            int pad = 250 * BytesPerMs / step;
            int a = Math.Max(0, first - pad) * step, b = Math.Min(n, last + 1 + pad) * step;
            var cut = new byte[b - a]; Buffer.BlockCopy(pcm, a, cut, 0, cut.Length);
            return Wrap(cut);
        }

        public static double Rms(byte[] pcm, int count)
        {
            int n = count / 2; if (n == 0) return 0;
            double sum = 0;
            for (int i = 0; i < n; i++) { double v = BitConverter.ToInt16(pcm, i * 2); sum += v * v; }
            return Math.Sqrt(sum / n);
        }
    }

    public interface IAudioSource : IDisposable
    {
        event Action<byte[]> Chunk;   // ~100 ms of PCM
        void Start();
    }

    /// <summary>A WAV file played into the pipeline in (nearly) real time, so voice can be tested without a person or a mic.</summary>
    public sealed class FileSource : IAudioSource
    {
        public event Action<byte[]> Chunk;
        readonly byte[] pcm; readonly double speed; volatile bool stop;
        public Action Done;
        public FileSource(byte[] wav, double speed) { pcm = Wav.Pcm(wav); this.speed = speed; }
        public void Start()
        {
            new Thread(() =>
            {
                const int step = 3200;
                for (int i = 0; i < pcm.Length && !stop; i += step)
                {
                    var c = new byte[Math.Min(step, pcm.Length - i)]; Buffer.BlockCopy(pcm, i, c, 0, c.Length);
                    Chunk?.Invoke(c);
                    Thread.Sleep((int)(100 / speed));
                }
                Done?.Invoke();
            }) { IsBackground = true, Name = "FileSource" }.Start();
        }
        public void Dispose() { stop = true; }
    }

    /// <summary>The default (or a named) microphone through winmm; no extra library needed.</summary>
    public sealed class MicSource : IAudioSource
    {
        [StructLayout(LayoutKind.Sequential)] struct WaveFormat { public ushort Tag, Channels; public uint Rate, AvgBytes; public ushort Align, Bits, Size; }
        [StructLayout(LayoutKind.Sequential)] struct WaveHdr { public IntPtr Data; public uint Length, Recorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] struct WaveInCaps { public ushort Mid, Pid; public uint Version; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public uint Formats; public ushort Channels, Reserved; }
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, int dev, ref WaveFormat f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Auto)] static extern int waveInGetDevCaps(IntPtr dev, ref WaveInCaps caps, int size);

        const int CallbackEvent = 0x50000, Done = 1, Buffers = 5, BufBytes = 3200;
        public event Action<byte[]> Chunk;
        readonly int device; IntPtr h; readonly IntPtr[] hdrs = new IntPtr[Buffers]; readonly IntPtr[] mem = new IntPtr[Buffers];
        readonly ManualResetEvent ev = new ManualResetEvent(false); volatile bool run;
        public string Error;

        public static List<string> Devices()
        {
            var l = new List<string>();
            for (int i = 0; i < waveInGetNumDevs(); i++) { var c = new WaveInCaps(); waveInGetDevCaps((IntPtr)i, ref c, Marshal.SizeOf(typeof(WaveInCaps))); l.Add(c.Name); }
            return l;
        }

        public MicSource(string name)
        {
            var all = Devices();
            var idx = string.IsNullOrEmpty(name) ? -1 : all.FindIndex(d => name.StartsWith(d, StringComparison.OrdinalIgnoreCase) || d.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            device = idx; // -1 = WAVE_MAPPER, the system default
        }

        public void Start()
        {
            var f = new WaveFormat { Tag = 1, Channels = 1, Rate = Wav.Rate, AvgBytes = Wav.Rate * 2, Align = 2, Bits = 16, Size = 0 };
            int r = waveInOpen(out h, device, ref f, ev.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
            if (r != 0) { Error = "no microphone (error " + r + ")"; return; }
            int size = Marshal.SizeOf(typeof(WaveHdr));
            for (int i = 0; i < Buffers; i++)
            {
                mem[i] = Marshal.AllocHGlobal(BufBytes); hdrs[i] = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(new WaveHdr { Data = mem[i], Length = BufBytes }, hdrs[i], false);
                waveInPrepareHeader(h, hdrs[i], size); waveInAddBuffer(h, hdrs[i], size);
            }
            run = true; waveInStart(h);
            new Thread(Loop) { IsBackground = true, Name = "MicSource" }.Start();
        }

        void Loop()
        {
            int size = Marshal.SizeOf(typeof(WaveHdr));
            while (run)
            {
                ev.WaitOne(250); ev.Reset();
                for (int i = 0; i < Buffers && run; i++)
                {
                    var hd = (WaveHdr)Marshal.PtrToStructure(hdrs[i], typeof(WaveHdr));
                    if ((hd.Flags & Done) == 0) continue;
                    var c = new byte[hd.Recorded]; Marshal.Copy(hd.Data, c, 0, c.Length);
                    waveInAddBuffer(h, hdrs[i], size);
                    try { Chunk?.Invoke(c); } catch { /* a listener's bug must not stop the mic */ }
                }
            }
        }

        public void Dispose()
        {
            if (!run && h == IntPtr.Zero) return;
            run = false; Thread.Sleep(60);
            if (h != IntPtr.Zero)
            {
                waveInReset(h); int size = Marshal.SizeOf(typeof(WaveHdr));
                for (int i = 0; i < Buffers; i++) if (hdrs[i] != IntPtr.Zero) { waveInUnprepareHeader(h, hdrs[i], size); Marshal.FreeHGlobal(hdrs[i]); Marshal.FreeHGlobal(mem[i]); hdrs[i] = IntPtr.Zero; }
                waveInClose(h); h = IntPtr.Zero;
            }
        }
    }

    /// <summary>"Automatic" microphone: while the talk key is held, every microphone records; on release the one with the most speech wins.
    /// In use 2026-10-26 the chosen mic was a silent virtual one (Virtual Desktop; the Windows default was Voicemod), and Whisper turned the
    /// silence into "Thank you." and Arabic subtitle credits. With all mics at once, the one the player speaks into always wins.</summary>
    public sealed class MultiMicCapture : IDisposable
    {
        readonly List<MicSource> mics = new List<MicSource>();
        readonly List<MemoryStream> bufs = new List<MemoryStream>();
        readonly List<string> names = new List<string>();
        public event Action<float> Level;

        public static MultiMicCapture Start(IList<string> deviceNames)
        {
            var m = new MultiMicCapture();
            foreach (var n in deviceNames)
            {
                var src = new MicSource(n); var buf = new MemoryStream(); int idx = m.bufs.Count;
                src.Chunk += c => { lock (buf) buf.Write(c, 0, c.Length); var l = m.Level; if (l != null) l((float)Math.Min(1.0, Wav.Rms(c, c.Length) / 6000.0)); };
                src.Start();
                if (src.Error != null) { src.Dispose(); continue; }   // a mic that cannot open (in use, unplugged) is skipped
                m.mics.Add(src); m.bufs.Add(buf); m.names.Add(n);
            }
            return m;
        }

        public int Count { get { return mics.Count; } }

        static double Weight(string name)
        {
            if (string.IsNullOrEmpty(name)) return 1.0;
            if (name.IndexOf("Voicemod", StringComparison.OrdinalIgnoreCase) >= 0) return 0.6;
            if (name.IndexOf("Broadcast", StringComparison.OrdinalIgnoreCase) >= 0) return 1.15;
            return 1.0;
        }

        /// <summary>Stops every mic and returns the loudest recording as a WAV (null when nothing was recorded), with its mic and loudness.</summary>
        public byte[] Finish(out string bestName, out double bestRms, out string report)
        {
            foreach (var m in mics) m.Dispose();
            bestName = ""; bestRms = 0; byte[] best = null; var parts = new List<string>();
            for (int i = 0; i < bufs.Count; i++)
            {
                byte[] pcm; lock (bufs[i]) pcm = bufs[i].ToArray();
                double rms = Wav.Rms(pcm, pcm.Length);
                parts.Add(names[i] + " " + (int)rms);
                // Voicemod may add voice effects; NVIDIA Broadcast is a noise-cleaned copy of a real mic: weigh them when they are close.
                if (pcm.Length > 0 && rms * Weight(names[i]) > bestRms * Weight(bestName)) { bestRms = rms; bestName = names[i]; best = pcm; }
            }
            report = string.Join(", ", parts);
            return best == null ? null : Wav.Wrap(best);
        }

        public void Dispose() { foreach (var m in mics) m.Dispose(); }
    }

    /// <summary>A blocking byte pipe the recognizer reads from while the mic writes to it.
    /// System.Speech only reads a stream that claims CanSeek, has a length, and returns the full count it asked for
    /// (a short read is taken as the end); found with a probe 2026-10-26.</summary>
    sealed class PcmPipe : Stream
    {
        readonly BlockingCollection<byte[]> q = new BlockingCollection<byte[]>(64);
        byte[] cur = new byte[0]; int pos; long total;
        public void Feed(byte[] b) { if (!q.IsAddingCompleted) q.TryAdd(b); }  // drops audio when the recognizer is far behind, never blocks the mic
        public void End() { q.CompleteAdding(); }
        public override int Read(byte[] buf, int off, int count)
        {
            int got = 0;
            while (got < count)
            {
                if (pos >= cur.Length) { try { cur = q.Take(); pos = 0; } catch (InvalidOperationException) { break; } }
                int n = Math.Min(count - got, cur.Length - pos);
                Buffer.BlockCopy(cur, pos, buf, off + got, n); pos += n; got += n;
            }
            total += got; return got;
        }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => int.MaxValue;
        public override long Position { get { return total; } set { } }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) { return total; }
        public override void SetLength(long v) { throw new NotSupportedException(); }
        public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
    }
    /// <summary>Listens for "Jarvis" (offline), runs the offline command grammar, and hands over utterances for speech-to-text.</summary>
    public sealed class Hearing : IDisposable
    {
        public event Action<string> OfflineCommand;     // "jarvis make it day" heard by the offline grammar
        public event Action<byte[], string> Utterance;  // a WAV to transcribe, and why: phrase | after-wake | ptt
        public event Action<bool> Listening;            // capture mode on / off (drives the dot)
        public event Action<float> Level;               // 0..1 loudness
        public volatile bool Muted;                     // true while Jarvis speaks, so he does not hear himself
        public event Action<string> Trace;              // what the recognizer thought, for the log and tuning
        public Func<bool> ConfirmCommands;              // true: a fixed-grammar command goes to speech-to-text first instead of running
        // In game 2026-10-26 a real voice scored 0.61 for "jarvis turn it to dawn" and 0.79-0.94 for "hey jarvis"; synthesized 0.91-0.94.
        public const float CmdConfidence = 0.6f, WakeConfidence = 0.7f;
        public string Error;

        readonly IAudioSource src; readonly bool wake;
        SpeechRecognitionEngine rec; readonly PcmPipe pipe = new PcmPipe();
        readonly object gate = new object();
        bool capturing, spoke, ptt; string why; MemoryStream cap; int totalMs, quietMs; double floor = 200;
        readonly Queue<byte[]> pre = new Queue<byte[]>();   // the last ~1.5 s: recognition lags the words, so a capture starts with what was just said

        public Hearing(IAudioSource source, bool useWakeWord) { src = source; wake = useWakeWord; }

        public static readonly string[] Phrases = BuildPhrases();

        static string[] BuildPhrases()
        {
            var l = new List<string>();
            foreach (var lead in new[] { "make it", "set the time to", "change it to", "turn it to" })
                foreach (var w in new[] { "day", "daytime", "morning", "night", "midnight", "evening", "sunset", "dawn", "sunrise" })
                    l.Add(lead + " " + w);
            l.AddRange(new[] { "make it rain", "stop the rain", "clear the sky", "clear the weather", "take everything off", "reset the time", "put the time back to normal" });
            return l.ToArray();
        }

        public bool Start()
        {
            try
            {
                if (wake)
                {
                    var us = CultureInfo.GetCultureInfo("en-US");
                    rec = SpeechRecognitionEngine.InstalledRecognizers().Any(r => r.Culture.Name == "en-US")
                        ? new SpeechRecognitionEngine(us) : new SpeechRecognitionEngine();
                    var jarvis = new Choices("jarvis", "hey jarvis", "ok jarvis");
                    rec.LoadGrammar(new Grammar(new GrammarBuilder(jarvis)) { Name = "wake" });
                    var cmd = new GrammarBuilder(jarvis); cmd.Append(new Choices(Phrases));
                    rec.LoadGrammar(new Grammar(cmd) { Name = "cmd" });
                    var wild = new GrammarBuilder(jarvis); wild.AppendWildcard();
                    rec.LoadGrammar(new Grammar(wild) { Name = "wild" });
                    rec.SpeechRecognized += OnRecognized;
                    rec.SpeechRecognitionRejected += OnRejected;
                    rec.SpeechHypothesized += (s, e) => Trace?.Invoke("hypothesis: '" + e.Result.Text + "'");
                    rec.SetInputToAudioStream(pipe, new SpeechAudioFormatInfo(Wav.Rate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                    rec.RecognizeAsync(RecognizeMode.Multiple);
                }
                src.Chunk += OnChunk;
                src.Start();
                var mic = src as MicSource;
                if (mic != null && mic.Error != null) { Error = mic.Error; return false; }
                return true;
            }
            catch (Exception e) { Error = e.Message; return false; }
        }

        void OnRecognized(object s, SpeechRecognizedEventArgs e)
        {
            var r = e.Result;
            Trace?.Invoke(r.Grammar.Name + ": '" + r.Text + "' conf " + r.Confidence.ToString("0.00") + (Muted ? " (muted)" : ""));
            if (Muted) return;
            switch (r.Grammar.Name)
            {
                // A fixed grammar force-fits noise into its phrases: in game a room's sound became "change it to evening" (2026-10-26).
                // Even at 0.89 it force-fit unrelated speech as "jarvis stop the rain" (owner, 2026-10-27), so with Whisper it only wakes.
                case "cmd":
                    if (r.Confidence < CmdConfidence || capturing) break;
                    if (ConfirmCommands != null && ConfirmCommands()) EmitPhrase(r.Audio); else OfflineCommand?.Invoke(r.Text);
                    break;
                case "wake": if (r.Confidence >= WakeConfidence) Begin("after-wake"); break;
                case "wild":
                    if (r.Confidence < WakeConfidence || capturing) break;
                    // The wildcard grammar also matches a lone "Jarvis"; then there is no "..." and the request is still to come.
                    if (r.Text.Contains("...")) EmitPhrase(r.Audio); else Begin("after-wake");
                    break;
            }
        }

        // One breath ("Jarvis try on a cowboy hat") often ends as a rejected phrase that starts with Jarvis: still a request.
        void OnRejected(object s, SpeechRecognitionRejectedEventArgs e)
        {
            var r = e.Result;
            Trace?.Invoke("rejected: '" + r.Text + "' conf " + r.Confidence.ToString("0.00"));
            if (Muted || capturing || r.Confidence < 0.3f || !LocalGrammar.HasWake(r.Text)) return;
            // Never run a REJECTED phrase as a command: "Jarvis try on a cowboy hat" is force-fit as "jarvis clear the weather" (0.51),
            // while a real "jarvis make it rain" was rejected at 0.59. A missed command beats a wrong one.
            EmitPhrase(r.Audio);
        }

        void EmitPhrase(RecognizedAudio a)
        {
            if (a == null) return;
            using (var ms = new MemoryStream()) { a.WriteToWaveStream(ms); Utterance?.Invoke(ms.ToArray(), "phrase"); }
        }

        public void PttDown() { ptt = true; Begin("ptt"); }
        public void PttUp() { if (ptt) { ptt = false; Finish(); } }

        void Begin(string reason)
        {
            lock (gate)
            {
                if (capturing) return;
                capturing = true; totalMs = 0; quietMs = 0; why = reason; cap = new MemoryStream();
                foreach (var p in pre) cap.Write(p, 0, p.Length);
                var last = pre.Count > 0 ? pre.Last() : null;
                spoke = last != null && Wav.Rms(last, last.Length) > Math.Max(450, floor * 2.5);   // already mid-request?
                pre.Clear();
            }
            Listening?.Invoke(true);
        }

        void Finish()
        {
            byte[] pcm; string reason;
            lock (gate) { if (!capturing) return; capturing = false; pcm = cap.ToArray(); reason = why; cap = null; }
            Listening?.Invoke(false);
            if (pcm.Length >= 400 * Wav.BytesPerMs) Utterance?.Invoke(Wav.Wrap(pcm), reason);
        }

        void OnChunk(byte[] c)
        {
            if (Muted) return;
            var rms = Wav.Rms(c, c.Length);
            Level?.Invoke((float)Math.Min(1.0, rms / 6000.0));
            bool loud = rms > Math.Max(450, floor * 2.5);
            if (!loud) floor = floor * 0.95 + rms * 0.05;
            bool done = false;
            lock (gate)
            {
                if (!capturing) { pre.Enqueue(c); if (pre.Count > 15) pre.Dequeue(); }
                if (capturing)
                {
                    cap.Write(c, 0, c.Length); totalMs += c.Length / Wav.BytesPerMs;
                    if (loud) { spoke = true; quietMs = 0; } else quietMs += c.Length / Wav.BytesPerMs;
                    if (!ptt) done = (spoke && quietMs >= 900) || totalMs > 9000 || (!spoke && totalMs > 4000);
                }
            }
            if (done) Finish();
            else if (!capturing && rec != null) pipe.Feed(c);   // the wake listener only hears while we are not already capturing
        }

        public void Dispose()
        {
            src.Chunk -= OnChunk; src.Dispose(); pipe.End();
            try { if (rec != null) { rec.RecognizeAsyncCancel(); rec.Dispose(); } } catch { }
        }
    }

    /// <summary>Offline speech-to-text of a captured utterance, for providers without Whisper (English only on most PCs).</summary>
    public static class LocalStt
    {
        public static string Last;   // the error of the last call, for the log

        /// <summary>Text for a held-key utterance: the command phrases first (dictation turned a clear "make it day" into
        /// "They could take"), dictation only when no command fits well.</summary>
        public static string Recognize(byte[] wav)
        {
            try
            {
                using (var e = new SpeechRecognitionEngine(CultureInfo.GetCultureInfo("en-US")))
                {
                    var gb = new GrammarBuilder(new Choices("jarvis", "hey jarvis", "ok jarvis"), 0, 1);
                    gb.Append(new Choices(Hearing.Phrases));
                    e.LoadGrammar(new Grammar(gb));
                    e.SetInputToWaveStream(new MemoryStream(wav));
                    var r = e.Recognize(TimeSpan.FromSeconds(10));
                    if (r != null && r.Confidence >= Hearing.CmdConfidence) return r.Text;
                }
            }
            catch (Exception ex) { Last = ex.GetType().Name + ": " + ex.Message; }
            return Dictate(wav);
        }

        static string Dictate(byte[] wav)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                using (var e = new SpeechRecognitionEngine(CultureInfo.GetCultureInfo("en-US")))
                {
                    e.LoadGrammar(new DictationGrammar());
                    e.SetInputToWaveStream(new MemoryStream(wav));
                    RecognitionResult r;   // Recognize() returns one phrase at a time; "Jarvis ... (pause) ... request" is two
                    while (sb.Length < 400 && (r = e.Recognize(TimeSpan.FromSeconds(10))) != null) sb.Append(sb.Length > 0 ? " " : "").Append(r.Text);
                }
            }
            catch (Exception ex) { Last = ex.GetType().Name + ": " + ex.Message; }   // end of stream throws on some builds: keep what was heard
            return sb.ToString();
        }
    }
    /// <summary>Jarvis's own entry in the Windows volume mixer. In use 2026-10-26 it sat at 0 (the owner heard nothing until they raised it).
    /// His loudness is the Volume setting in the game's menu, so his mixer entry is kept at full and unmuted, and named "Valyx Jarvis".</summary>
    public static class MixerEntry
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator { }
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator { int EnumAudioEndpoints(int flow, int state, out IntPtr devices); int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device); }
        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice { int Activate(ref Guid iid, int ctx, IntPtr param, [MarshalAs(UnmanagedType.IUnknown)] out object o); }
        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2 { int GetAudioSessionControl(IntPtr a, int b, out IntPtr c); int GetSimpleAudioVolume(IntPtr a, int b, out IntPtr c); int GetSessionEnumerator(out IAudioSessionEnumerator e); }
        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator { int GetCount(out int n); int GetSession(int i, out IAudioSessionControl2 s); }
        [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            int GetState(out int s); int GetDisplayName(out IntPtr n); int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string n, ref Guid ctx);
            int GetIconPath(out IntPtr p); int SetIconPath(IntPtr p, ref Guid ctx); int GetGroupingParam(out Guid g); int SetGroupingParam(ref Guid g, ref Guid ctx);
            int RegisterAudioSessionNotification(IntPtr n); int UnregisterAudioSessionNotification(IntPtr n);
            int GetSessionIdentifier(out IntPtr s); int GetSessionInstanceIdentifier(out IntPtr s); int GetProcessId(out uint pid);
        }
        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume { int SetMasterVolume(float v, ref Guid ctx); int GetMasterVolume(out float v); int SetMute(bool m, ref Guid ctx); int GetMute(out bool m); }

        /// <summary>Call while (or just after) Jarvis plays: Windows only lists an app once it has made sound. Returns what it changed, or "".</summary>
        public static string Fix() { return Apply(null, false); }

        /// <summary>Tests only: put this app's mixer entry at a volume and mute state (the fix must undo it). Reads back as "volume|muted|name".</summary>
        public static string Force(float volume, bool mute) { return Apply(volume, mute); }

        static string Apply(float? force, bool forceMute)
        {
            try
            {
                var en = (IMMDeviceEnumerator)new DeviceEnumerator(); IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return "";
                var iid = typeof(IAudioSessionManager2).GUID; object o;
                dev.Activate(ref iid, 23, IntPtr.Zero, out o);
                IAudioSessionEnumerator sessions; ((IAudioSessionManager2)o).GetSessionEnumerator(out sessions);
                int n; sessions.GetCount(out n);
                uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id; var none = Guid.Empty; string changed = "";
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 s; sessions.GetSession(i, out s); uint pid; s.GetProcessId(out pid);
                    if (pid != me) continue;
                    var vol = (ISimpleAudioVolume)s; float v; bool muted;
                    if (force.HasValue) { if (!float.IsNaN(force.Value)) { vol.SetMasterVolume(force.Value, ref none); vol.SetMute(forceMute, ref none); } vol.GetMasterVolume(out v); vol.GetMute(out muted); IntPtr name; s.GetDisplayName(out name); return v.ToString("0.00") + "|" + muted + "|" + Marshal.PtrToStringUni(name); }
                    s.SetDisplayName("Valyx Jarvis", ref none);
                    vol.GetMasterVolume(out v); vol.GetMute(out muted);
                    if (v < 0.99f) { vol.SetMasterVolume(1f, ref none); changed += "mixer volume was " + (int)(v * 100) + "%, set to 100%. "; }
                    if (muted) { vol.SetMute(false, ref none); changed += "it was muted, unmuted. "; }
                }
                return changed;
            }
            catch (Exception e) { return "mixer check failed: " + e.Message; }
        }
    }

    /// <summary>Jarvis's voice and little tones. Heard on this PC only, never sent into the game.</summary>
    public sealed class Speaker : IDisposable
    {
        readonly Settings s; SpeechSynthesizer synth; SoundPlayer player;
        public string Problem = "", ProblemUrl = "";   // why the human voice is not in use, for the menu
        // Languages whose human voice waits for the player to accept the model's terms (Groq asks once per model: English and Arabic apart).
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> TermsNeeded = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        /// <summary>A one-word request that plays nothing, to learn early whether this language's human voice needs its terms accepted.</summary>
        public async Task Probe(bool arabic)
        {
            var p = Providers.ById(s.Provider); var key = s.Key; var lang = arabic ? "ar" : "en";
            var model = arabic ? p.TtsModelAr : p.TtsModel; var voices = arabic ? p.VoicesAr : p.Voices;
            if (s.VoiceEngine != "ai" || string.IsNullOrEmpty(model) || voices.Length == 0 || key.Length == 0) { string x; TermsNeeded.TryRemove(lang, out x); return; }
            try { await Api.Speech(p, s, key, model, voices[0], arabic ? "تم" : "ok", CancellationToken.None).ConfigureAwait(false); string x; TermsNeeded.TryRemove(lang, out x); }
            catch (ApiException e) { if (e.Message.IndexOf("terms", StringComparison.OrdinalIgnoreCase) >= 0 && p.Id == "groq") TermsNeeded[lang] = TermsUrl(model); }
            catch { }
        }

        static string TermsUrl(string model) { return "https://console.groq.com/playground?model=" + Uri.EscapeDataString(model); }
        public Action<string> Log = m => { };
        DateTime aiRetryAt = DateTime.MinValue;
        public Speaker(Settings settings) { s = settings; }

        public static List<string> Voices()
        {
            try { using (var t = new SpeechSynthesizer()) return t.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList(); }
            catch { return new List<string>(); }
        }

        public static bool HasVoiceFor(string lang)
        {
            try { using (var t = new SpeechSynthesizer()) return t.GetInstalledVoices().Any(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == lang); }
            catch { return false; }
        }

        /// <summary>Speaks and returns when finished: the provider's human voice when it has one, else a Windows voice.
        /// False when there is nothing to speak with.</summary>
        public async Task<bool> Say(string text, bool arabic)
        {
            if (!s.Speak || string.IsNullOrWhiteSpace(text) || s.Volume == 0) return false;
            Stop();
            CheckMixer();
            if (s.VoiceEngine == "ai" && DateTime.UtcNow >= aiRetryAt && await SayAi(text, arabic).ConfigureAwait(false)) return true;
            return await SayWindows(text, arabic).ConfigureAwait(false);
        }

        async Task<bool> SayAi(string text, bool arabic)
        {
            var p = Providers.ById(s.Provider); var key = s.Key;
            var model = arabic ? p.TtsModelAr : p.TtsModel; var voices = arabic ? p.VoicesAr : p.Voices;
            if (string.IsNullOrEmpty(model) || voices.Length == 0 || key.Length == 0) return false;
            var voice = arabic ? s.AiVoiceAr : s.AiVoice; if (Array.IndexOf(voices, voice) < 0) voice = voices[0];
            byte[] wav;
            try { wav = await Api.Speech(p, s, key, model, voice, text, CancellationToken.None).ConfigureAwait(false); Problem = ""; ProblemUrl = ""; string x; TermsNeeded.TryRemove(arabic ? "ar" : "en", out x); }
            catch (ApiException e)
            {
                bool terms = e.Message.IndexOf("terms", StringComparison.OrdinalIgnoreCase) >= 0;
                Problem = terms ? "terms" : e.Kind.ToString();
                ProblemUrl = terms && p.Id == "groq" ? TermsUrl(model) : "";
                if (terms && p.Id == "groq") TermsNeeded[arabic ? "ar" : "en"] = ProblemUrl;
                aiRetryAt = DateTime.UtcNow.AddMinutes(terms ? 2 : 1);   // do not stall every answer; try again later (terms may get accepted)
                Log("human voice unavailable (" + e.Kind + " " + e.Message + "); using a Windows voice");
                return false;
            }
            catch (OperationCanceledException) { return false; }
            wav = Wav.Scale(wav, s.Volume / 100.0);
            return await Task.Run(() =>
            {
                try { var pl = new SoundPlayer(new MemoryStream(wav)); player = pl; pl.PlaySync(); return true; }
                catch (Exception e) { Log("could not play the voice: " + e.Message); return false; }
            }).ConfigureAwait(false);
        }

        Task<bool> SayWindows(string text, bool arabic)
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                synth = new SpeechSynthesizer { Volume = s.Volume, Rate = s.VoiceRate };
                bool picked = false;
                if (!string.IsNullOrEmpty(s.VoiceName)) { try { synth.SelectVoice(s.VoiceName); picked = true; } catch { } }
                if (!picked)
                {
                    var want = arabic ? "ar" : "en";
                    var v = synth.GetInstalledVoices().FirstOrDefault(x => x.Enabled && x.VoiceInfo.Culture.TwoLetterISOLanguageName == want);
                    if (v == null && arabic) { tcs.SetResult(false); return tcs.Task; }   // no Arabic voice installed: show the text only
                    if (v != null) synth.SelectVoice(v.VoiceInfo.Name);
                }
                var mine = synth;
                mine.SpeakCompleted += (a, b) => tcs.TrySetResult(true);
                mine.SpeakAsync(text);
            }
            catch { tcs.TrySetResult(false); }
            return tcs.Task;
        }

        /// <summary>Once playback has begun (Windows lists the app only then), make sure the mixer is not silencing Jarvis.</summary>
        void CheckMixer()
        {
            Task.Run(async () => { await Task.Delay(400).ConfigureAwait(false); var r = MixerEntry.Fix(); if (r.Length > 0) Log(r.Trim()); });
        }

        public void Stop()
        {
            try { if (synth != null) { synth.SpeakAsyncCancelAll(); synth.Dispose(); } } catch { }
            try { if (player != null) player.Stop(); } catch { }
            synth = null; player = null;
        }

        /// <summary>Two quick tones: rising = listening, falling = done.</summary>
        public void Chime(bool rising) { Chime(rising ? 660 : 880, rising ? 880 : 660, 0.2); }

        /// <summary>"Done" for a command that needs no words: two short bright notes.</summary>
        public void ChimeDone() { CheckMixer(); Chime(988, 1319, 0.16); }

        void Chime(double f1, double f2, double seconds)
        {
            if (!s.Chime || s.Volume == 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int n = (int)(Wav.Rate * seconds); var pcm = new byte[n * 2]; double amp = 9000.0 * s.Volume / 100.0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)Wav.Rate, f = i < n / 2 ? f1 : f2;
                        double env = Math.Min(1.0, Math.Min(t * 200, (seconds - t) * 200));
                        var v = (short)(Math.Sin(2 * Math.PI * f * t) * amp * env);
                        pcm[i * 2] = (byte)(v & 0xff); pcm[i * 2 + 1] = (byte)(v >> 8);
                    }
                    using (var p = new SoundPlayer(new MemoryStream(Wav.Wrap(pcm)))) p.PlaySync();
                }
                catch { }
            });
        }

        public void Dispose() { Stop(); }
    }
}