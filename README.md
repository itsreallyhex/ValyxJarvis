# Valyx Jarvis

**Mods shouldn't need a subscription.** Valyx Jarvis is a free, open-source voice assistant for Gorilla Tag (PC, BepInEx).
Say *"Jarvis, make it night"* and it happens. Everything Jarvis changes is on **your screen only**; other players never see it.

Part of Valyx, a community for Gorilla Tag modding: join the [Discord](https://discord.gg/C2ffEYHzPr) for help, ideas and updates, or visit [valyx.pages.dev](https://valyx.pages.dev).

[العربية](#العربية)

## What you need

- Gorilla Tag on PC (Steam), Windows 10 or 11, and [BepInEx 5](https://github.com/BepInEx/BepInEx).
- A microphone.
- **Your own AI key.** Jarvis does not come with one. Get a free one from **Groq** (no card needed, recommended: it also hears Arabic and has a human voice), **Google Gemini** or **OpenRouter**, or run **Ollama** on your own PC. The menu has a **Get a free key** button.
  Without a key only the basic English commands work (time, weather, try-on), and he hears you less well.

## What it does

- **Time of day:** *"Jarvis, make it day / night / sunset / dawn"*, or back to *"normal time"*.
- **Weather:** *"Jarvis, make it rain"*, *"stop the rain"*.
- **Try on cosmetics:** *"Jarvis, try on a party hat"*, *"take it off"*. Only you see the try-on. If he can't find the item, he names close ones.
- **Travel:** *"Jarvis, take me to Canyons"*, any map in the game. In a public lobby he leaves the lobby first.
- **Room questions:** how many players, who is here, the room code, the game mode, how many are lava and how many are left (counts only), where am I.
- **Chat:** ask him anything; he answers out loud, in English or Gulf Arabic.
- **Settings by voice:** *"Jarvis, open settings"*.
- **A floating dot** shows when Jarvis listens, thinks and talks, with a small panel showing what he heard and said.

## Using it

1. Put `ValyxJarvis.dll` in `Gorilla Tag\BepInEx\plugins` and start the game.
2. Press **F8** to open Jarvis's menu (on your PC screen, also when SteamVR is running).
3. Press **Get a free key**, paste your key, press **Save**. The provider is picked from the key by itself.
4. Talk:
   - say **"Jarvis"** and your request: he always listens for his name and only answers when he hears it, or
   - hold **V** while you speak (you can change the key in the menu), or
   - in VR, **hold the left thumbstick in** while you speak, or
   - type in the menu.

## Privacy

- Listening for "Jarvis" happens on your PC. Nothing is sent anywhere until he hears his name or you hold the talk key.
- What you say after that goes to the AI provider you chose, and only to it.
- Your key is stored encrypted for your Windows user (`%APPDATA%\ValyxJarvis`). It is never sent to other players or to Valyx.

## Good to know

- **Human voice:** with Groq, Jarvis talks with a human-sounding voice. Groq asks you to accept its terms once per voice language; Jarvis shows a popup with the page. Until then, or when the free daily limit is used up, Windows' own voice speaks.
- **Free keys have limits.** If you talk a lot, the provider may make you wait a minute.
- Jarvis uses a small helper program, `ValyxJarvis.exe`, packed inside the DLL and unpacked to `%LOCALAPPDATA%\ValyxJarvis` on first start. It has no window and closes when the game closes. Some antivirus programs are wary of unsigned programs; if Jarvis says it cannot start its helper, check your antivirus.

## Credits and licences

- Valyx Jarvis is free software under the GNU General Public License v3.0 (`LICENSE`).
- The Arabic text shaping comes from [GorillaHurouf](https://github.com/musaed-dev/GorillaHurouf) (MIT).
- The Arabic font is Noto Sans Arabic (SIL Open Font License 1.1, `Fonts/OFL.txt`).
- `Beacon.cs` is GPL-3.0.

Not affiliated with Another Axiom.

---

<div dir="rtl">

## العربية

**المودات ما تحتاج اشتراك.** فاليكس جارفيس مساعد صوتي مجاني ومفتوح المصدر لقورلا تاق على الكمبيوتر (BepInEx).
قل *"جارفيس، خله ليل"* ويصير. كل شي يغيره جارفيس يكون **على شاشتك أنت بس**، وما يشوفه أحد غيرك.

### وش تحتاج

- قورلا تاق على الكمبيوتر (ستيم)، ويندوز 10 أو 11، و[BepInEx 5](https://github.com/BepInEx/BepInEx).
- مايك.
- **مفتاح ذكاء اصطناعي خاص فيك.** جارفيس ما يجي معه مفتاح. خذ واحد مجاني من **Groq** (ما يحتاج بطاقة، وننصح فيه: يسمع العربي وفيه صوت بشري)، أو **Gemini** أو **OpenRouter**، أو شغل **Ollama** على جهازك. في القائمة زر **مفتاح مجاني**.
  بدون مفتاح تشتغل بس الأوامر الأساسية بالإنجليزي (الوقت، الطقس، التجربة)، ويسمعك أقل.

### وش يسوي

- **وقت اليوم:** نهار، ليل، غروب، فجر، أو رجوع للوقت الطبيعي.
- **الطقس:** مطر أو جو صافي.
- **تجربة الملابس:** *"جرب قبعة حفلة"* أو *"شيلها"*. التجربة تشوفها أنت بس.
- **التنقل:** *"ودني الكانيون"*، أي ماب في اللعبة. في الروم العام يطلعك من الروم أول.
- **أسئلة الروم:** كم لاعب، مين موجود، كود الروم، المود، كم واحد لافا وكم باقي (أرقام بس)، وين أنا.
- **سوالف:** اسأله أي شي ويرد بالصوت، بالعربي الخليجي أو الإنجليزي.
- **نقطة عائمة** توضح متى يسمع ويفكر ويتكلم، مع لوحة صغيرة فيها وش سمع ووش قال.

### الاستخدام

1. حط `ValyxJarvis.dll` في `Gorilla Tag\BepInEx\plugins` وشغل اللعبة.
2. اضغط **F8** عشان تفتح قائمة جارفيس (على شاشة الكمبيوتر، حتى لو SteamVR شغال).
3. اضغط **مفتاح مجاني**، الصق مفتاحك واضغط **حفظ**.
4. تكلم: قل **"جارفيس"** وطلبك (يسمع اسمه دايماً وما يرد إلا إذا سمعه)، أو امسك **V** وتكلم، أو في الـVR اضغط **العصا اليسرى** وأنت تتكلم، أو اكتب في القائمة.

### الخصوصية

- الاستماع لاسم "جارفيس" يصير على جهازك، وما ينرسل شي إلا إذا سمع اسمه أو مسكت زر الكلام.
- مفتاحك محفوظ ومشفر لمستخدم ويندوز حقك، وما ينرسل لأي لاعب ولا لفاليكس.

</div>
