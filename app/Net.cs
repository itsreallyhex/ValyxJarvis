// Valyx Jarvis desktop app: talking to the AI provider. OpenAI-compatible chat (Groq, Gemini, OpenRouter, Ollama, OpenAI, custom),
// Anthropic's own format, the model list, and speech-to-text on providers that have a Whisper endpoint.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ValyxJarvis
{
    public enum ApiFail { None, NoKey, KeyRejected, RateLimited, Network, Server, BadReply }

    public sealed class ApiException : Exception
    {
        public readonly ApiFail Kind;
        public ApiException(ApiFail kind, string detail) : base(detail) { Kind = kind; }
    }

    public static class Api
    {
        static readonly HttpClient Http = Make();

        static HttpClient Make()
        {
            // .NET Framework 4.8 does not always offer TLS 1.2/1.3 by default.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ValyxJarvis/1.0");
            return c;
        }

        public static string BaseUrl(Provider p, Settings s)
        {
            var url = (p.Id == "custom" || (p.Id == "ollama" && s.BaseUrl.Length > 0)) && s.BaseUrl.Length > 0 ? s.BaseUrl : p.BaseUrl;
            return url.TrimEnd('/');
        }

        static HttpRequestMessage Req(HttpMethod m, string url, Provider p, string key)
        {
            var r = new HttpRequestMessage(m, url);
            if (p.Anthropic) { r.Headers.Add("x-api-key", key); r.Headers.Add("anthropic-version", "2023-06-01"); }
            else if (key.Length > 0) r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            if (p.Id == "openrouter") { r.Headers.Add("X-Title", "Valyx Jarvis"); }
            return r;
        }

        static async Task<string> Send(HttpRequestMessage r, CancellationToken ct)
        {
            HttpResponseMessage resp;
            try { resp = await Http.SendAsync(r, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { throw new ApiException(ApiFail.Network, e.Message); }
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return body;
            var code = (int)resp.StatusCode;
            var kind = code == 401 || code == 403 ? ApiFail.KeyRejected : code == 429 ? ApiFail.RateLimited : code >= 500 ? ApiFail.Server : ApiFail.BadReply;
            throw new ApiException(kind, code + " " + Short(body));
        }

        static string Short(string s) { s = (s ?? "").Replace('\n', ' '); return s.Length > 200 ? s.Substring(0, 200) : s; }

        /// <summary>The provider's own model ids. Also the cheapest way to prove a key works.</summary>
        public static async Task<List<string>> ListModels(Provider p, Settings s, string key, CancellationToken ct)
        {
            var url = BaseUrl(p, s) + (p.Anthropic ? "/v1/models" : "/models");
            var body = await Send(Req(HttpMethod.Get, url, p, key), ct).ConfigureAwait(false);
            var ids = new List<string>();
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(body);
                object data;
                if (root.TryGetValue("data", out data) || root.TryGetValue("models", out data))
                    foreach (var item in (System.Collections.IEnumerable)data)
                    {
                        var d = item as Dictionary<string, object>;
                        object id;
                        if (d != null && (d.TryGetValue("id", out id) || d.TryGetValue("name", out id)) && id != null)
                            ids.Add(id.ToString().Replace("models/", ""));
                    }
            }
            catch (Exception e) { throw new ApiException(ApiFail.BadReply, e.Message); }
            return ids;
        }

        public sealed class Turn { public string Role, Text; }

        /// <summary>One chat call; returns the model's raw text (Brain.ParseReply reads it).</summary>
        public static async Task<string> Chat(Provider p, Settings s, string key, string model, string system, IList<Turn> history, string user, CancellationToken ct)
        {
            if (p.NeedsKey && key.Length == 0) throw new ApiException(ApiFail.NoKey, "no key");
            var ser = new JavaScriptSerializer();
            var msgs = new List<object>();
            if (!p.Anthropic) msgs.Add(new Dictionary<string, object> { { "role", "system" }, { "content", system } });
            foreach (var t in history) msgs.Add(new Dictionary<string, object> { { "role", t.Role }, { "content", t.Text } });
            msgs.Add(new Dictionary<string, object> { { "role", "user" }, { "content", user } });
            var body = new Dictionary<string, object> { { "model", model }, { "messages", msgs }, { "max_tokens", 300 } };
            if (p.Anthropic) body["system"] = system; else body["temperature"] = 0.6;   // warmer, less robotic wording; the JSON is still parsed strictly
            if (model.IndexOf("gpt-oss", StringComparison.OrdinalIgnoreCase) >= 0) body["reasoning_effort"] = "low";   // a voice answer should not think for long
            var url = BaseUrl(p, s) + (p.Anthropic ? "/v1/messages" : "/chat/completions");
            var req = Req(HttpMethod.Post, url, p, key);
            req.Content = new StringContent(ser.Serialize(body), Encoding.UTF8, "application/json");
            var text = await Send(req, ct).ConfigureAwait(false);
            try
            {
                var root = ser.Deserialize<Dictionary<string, object>>(text);
                if (p.Anthropic)
                {
                    var content = (System.Collections.IEnumerable)root["content"];
                    foreach (var c in content) { var d = (Dictionary<string, object>)c; if ((d["type"] as string) == "text") return (string)d["text"]; }
                }
                else
                {
                    var choice = (Dictionary<string, object>)((System.Collections.IList)root["choices"])[0];
                    return (string)((Dictionary<string, object>)choice["message"])["content"];
                }
            }
            catch (Exception e) { throw new ApiException(ApiFail.BadReply, e.Message); }
            throw new ApiException(ApiFail.BadReply, "no text in reply");
        }

        /// <summary>Text to speech through an OpenAI-style /audio/speech (Groq Orpheus, OpenAI): a WAV file.</summary>
        public static async Task<byte[]> Speech(Provider p, Settings s, string key, string model, string voice, string text, CancellationToken ct)
        {
            var body = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "model", model }, { "voice", voice }, { "input", text }, { "response_format", "wav" } });
            var req = Req(HttpMethod.Post, BaseUrl(p, s) + "/audio/speech", p, key);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            HttpResponseMessage resp;
            try { resp = await Http.SendAsync(req, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { throw new ApiException(ApiFail.Network, e.Message); }
            var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return bytes;
            var code = (int)resp.StatusCode; var msg = Short(Encoding.UTF8.GetString(bytes));
            throw new ApiException(code == 401 || code == 403 ? ApiFail.KeyRejected : code == 429 ? ApiFail.RateLimited : code >= 500 ? ApiFail.Server : ApiFail.BadReply, code + " " + msg);
        }

        public sealed class Heard { public string Text = "", Language = ""; }

        /// <summary>Speech to text on providers with an OpenAI-style /audio/transcriptions (Groq, OpenAI). Null language = detect (forcing a
        /// language made mixed Arabic/English worse); verbose_json returns the detected language so a strange guess can be retried.</summary>
        public static async Task<Heard> Transcribe(Provider p, Settings s, string key, byte[] wav, string language, CancellationToken ct, string prompt = null)
        {
            if (string.IsNullOrEmpty(p.SttModel)) throw new ApiException(ApiFail.BadReply, "provider has no speech endpoint");
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(wav);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            form.Add(file, "file", "speech.wav");
            form.Add(new StringContent(p.SttModel), "model");
            form.Add(new StringContent("verbose_json"), "response_format");
            form.Add(new StringContent("0"), "temperature");
            if (!string.IsNullOrEmpty(language) && language != "auto") form.Add(new StringContent(language), "language");
            if (!string.IsNullOrEmpty(prompt)) form.Add(new StringContent(prompt), "prompt");
            var req = Req(HttpMethod.Post, BaseUrl(p, s) + "/audio/transcriptions", p, key);
            req.Content = form;
            var text = await Send(req, ct).ConfigureAwait(false);
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text);
                object t, l;
                return new Heard { Text = d.TryGetValue("text", out t) ? Convert.ToString(t) : "", Language = d.TryGetValue("language", out l) && l != null ? LangCode(Convert.ToString(l)) : "" };
            }
            catch (Exception e) { throw new ApiException(ApiFail.BadReply, e.Message); }
        }

        /// <summary>"english" / "Arabic" / "ar" -> "en" / "ar"; anything else lower-cased as given.</summary>
        public static string LangCode(string l)
        {
            l = (l ?? "").Trim().ToLowerInvariant();
            if (l == "english" || l == "en") return "en";
            if (l == "arabic" || l == "ar") return "ar";
            return l;
        }
    }
}
