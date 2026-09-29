# Retro Radio

A music player that looks like an early-2000s double-DIN car stereo: brushed silver
faceplate, glowing dot-matrix VFD display, 3D spectrum analyzer, blue-lit volume knob.

## Run

```
dotnet run -c Release
```

Or build a standalone exe (lands in `bin/Release/net8.0-windows/win-x64/publish/`):

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Needs the .NET 8 SDK (or runtime for the exe). Plays MP3, WAV, FLAC, M4A/AAC, WMA, AIFF.

## Controls

Drag files/folders onto the radio, or use OPEN / FOLDER. Drag the window anywhere on
the faceplate. Your playlist, volume, and settings are saved on POWER.

| Button | Key | What it does |
|---|---|---|
| Center play button | Space | Play / pause |
| ◀◀ / ▶▶ chevrons | P / N | Previous / next track |
| SEEK | ← / → | Skip back / forward 5 s (hold to repeat) |
| VOLUME knob, VOL +/-, mouse wheel | ↑ / ↓ | Volume (0–40) |
| SPEED knob (drag or scroll it; double-click = 1x) | [ / ] / \ | Playback speed 0.5x–2x, tape style (pitch follows speed) |
| OPEN / FOLDER | O / F | Add files / a whole folder |
| LIST | L | Track list on the display (wheel/arrows, double-click or Enter plays) |
| SETTINGS | C | Settings on the display, in sections: RADIO (model, color, lighting, visualizer…), SOUND (EQ, loudness, reverb, outside car, crossfade, key/knob sounds), PLAYBACK (sleep timer, smart shuffle, BPM, repeat/shuffle, seek step), DISPLAY (idle clock, two-color, bass pulse, auto night, flip screen, lyrics…), POWER (startup/shutdown sounds and animations, voice lines), SYSTEM (language, size, mini mode, tray, start with Windows) and ONLINE. Click a value (or ←/→) to change it, right-click or Esc to go back. |
| VIS | V | Visualizer: 3D fan · 3D dot grid · bars · scope · circle · VU meters · spinning CD · fire · starfield · video |
| — | K | SoundCloud menu |
| — | E | Equalizer screen (bass / mid / treble + presets) |
| — | / or Ctrl+F | Search your music (type to filter the list) |
| — | D | Mini mode (just the display) |
| MUTE / SHUF / RPT / STOP | M / H / R / S | Mute · shuffle · repeat off/all/one · stop |
| P-TIME box on the display | T | Toggle elapsed / remaining time |
| Progress bar on the display | | Click to seek |
| POWER | Esc minimizes | Quit |

## Languages, JDM style, sounds and animations

- **LANGUAGE**: English, Deutsch, Español, Français, Polski, Svenska, Русский or 日本語 for the radio's own
  text (song titles are never changed). The dot-matrix display shows any script: Latin, Cyrillic and
  Greek capitals as hand-made 5x7 glyphs, and Japanese, Chinese, Korean and others as fine dots.
- **JDM STYLE** (on by default): Japanese greetings on power-on/off that match the sound (ようこそ, おかえり,
  こんばんは… / さようなら, おやすみ, 安全運転で…), Japanese radio names and Japanese boot text.
- **STARTUP SOUND**: WELCOME, OKAERI, OHAYOU, KONBANWA, ENGINE, RANDOM or CUSTOM (your own file);
  **SHUTDOWN SOUND**: SYSTEM OFF, OTSUKARE, MATA, OYASUMI, ANZEN UNTEN or RANDOM; **SOUND VOLUME** sets
  their level (or follows the volume knob). The sounds are built into the program (`Sounds/`).
- **STARTUP ANIM**: classic, scanner, rain, static, terminal, bounce; **SHUTDOWN ANIM**: TV off, fade,
  dissolve, wipe, fall. Changing either shows a preview.

## Sound, playback and display extras

- **EQ** (key E): bass / mid / treble knobs, presets (FLAT, ROCK, POP, JAZZ, CLUB, JDM BASS, VOCAL, CUSTOM) and
  **LOUDNESS**, which adds bass and treble more at low volume like a real car stereo.
- **REVERB**: room, hall, garage or echo. **OUTSIDE CAR**: the muffled sound of music heard from outside the car.
- **CROSSFADE**: 0–10 s between songs. **SMART SHUFFLE** avoids recent songs and artists.
- **SLEEP TIMER**: 15–90 min, then the radio powers off (with its shutdown animation and sound).
- **INTERNET RADIO** (ONLINE or the SoundCloud menu): thousands of stations from radio-browser.info — top
  stations, Japan, city pop, J-pop, anime, lofi, synthwave, 80s, jazz and more — with the live song title.
- **BPM** display, **IDLE CLOCK** (a big clock when nothing plays), **TWO-COLOR** VFD, **BASS PULSE** (the
  faceplate lights pulse on the beat), **AUTO NIGHT** (night lighting from sunset to sunrise), **FLIP SCREEN**
  (modern radios' screen flips down when powering on), and a CD that flies into the slot when you drop files.
- **KEY SOUND / KNOB SOUND**: built-in clicks, beeps and detents, or your own `.wav`/`.mp3` files dropped in
  `%AppData%\RetroRadio\clicks` (they show up in the list as FILE: name).
- **VOICE LINES**: a Japanese navigation-style voice that greets you for the time of day (おはよう / こんにちは /
  こんばんは / おやすみ) and now and then says something that fits: night drives, long sessions (take a
  break), the last song, the speed knob turned up (turbo ready)… VOICE EVERY sets how often.
- **FILES ONLY** (SETTINGS → ONLINE): turns off Spotify, SoundCloud, internet radio and online lyrics, so the
  radio only plays your own files and never goes online. Online entries in the playlist are skipped.
- EQ boosts come with automatic headroom and a look-ahead limiter, so even +12 dB bass at full volume stays clean.
- **FRAME RATE** (SETTINGS → SYSTEM): AUTO (60 fps while playing, 30 when idle), 60 or 30 FPS. Only the display is
  redrawn each frame, and display videos stream from the file instead of being loaded into memory.
- **MINI MODE** (key D), **TRAY ICON** (minimize to the notification area) and **START WITH WINDOWS**.

## Radio models

SETTINGS → RADIO MODEL switches the whole radio: its size and shape, where every key and knob
sits, the materials and the lighting. 39 are built in:

Classic (dot-matrix VFD screens): Silver 2000 · Midnight 99 · Neon 2003 · Woodgrain 84 · Carbon Race ·
Royal 76 · Field Unit · Aura White · Imperial Gold · Xplosion · Bel-Aire 57 · Shadow S1 · Groove 72 ·
Hauler HX · Bubble Y2K

Modern (high-resolution colour screens with album covers, tags, a clock and modern menus):
Prism X9 · Nova Tab · Aurora Strip (single-DIN) · Vector Portrait (tall portrait screen)

New: Kensei DDX-9 · Nakamura TD-1200 · Xplode GT-2001 · Carrozza DEH-9 · Kaido Racer · Touge 86 ·
Sakura Pop · Dekotora Gold · Vapor 1989 · Miami Nights · Polar White · Milspec MS-4 · Cruisemaster 70 ·
Titan Amp 2400 · Pocketsonic PS-89 · Suburbia 99 · Lowrider Gold · Tapeworks 84, and the modern Orbit O8
and Nebula X.

Album covers come from the music file's tags, a cover.jpg / folder.jpg next to it, or SoundCloud /
Spotify artwork. SETTINGS → LIGHTING switches between DAY and NIGHT.

## Radio Designer (make your own)

`RadioDesigner/` is a third program for building a completely custom radio: window size, faceplate
shape, finish (brushed metal, matte, gloss, wood, carbon…), materials, colours, fonts, where the
display goes and how big it is, and every key and knob — drag to move, drag corners to resize.
Start from any built-in model (New from…), then **Install into radio**. In the radio pick it under
SETTINGS → RADIO MODEL, or drag a `.radio.json` file onto the radio (SETTINGS → IMPORT RADIO works too).
`Radio Templates/` has all 15 built-in radios as editable files.

```
dotnet run -c Release --project RadioDesigner
```

## Videos on the display

`VideoConverter/` is a second program, **Radio Video Converter**. It turns any video into a
`.rdv` dot-matrix animation (192×48 dots) with a live preview. It needs ffmpeg (ffmpeg.org).
Drag the `.rdv` onto the radio or pick it in SETTINGS → DISPLAY VIDEO; VIS switches to VIDEO.

```
dotnet run -c Release --project VideoConverter
```

Tick **Color** in the converter for full-color videos: every dot gets its own color (256 colors,
dithered so gradients stay smooth). Color videos need this version of Retro Radio; one-color
videos keep working everywhere.

## SoundCloud / Spotify

SETTINGS → MUSIC SERVICE picks SoundCloud or Spotify; SETTINGS → ONLINE MUSIC (or K) opens its menu.

**Spotify:** Spotify doesn't allow other apps to stream its audio, so the radio remote-controls your
Spotify app instead: browse/search on the radio, the Spotify app plays, and the radio's display,
buttons and volume knob follow and control it (the analyzer listens to your PC's sound). Create a
free app at https://developer.spotify.com/dashboard, add the redirect URI
`http://127.0.0.1:53682/callback`, and paste its Client ID (no secret). Controlling playback needs
Spotify Premium.

**SoundCloud:** SoundCloud only allows registered apps, so the first time:
register a free app at https://soundcloud.com/you/apps, set its redirect URI to
`http://127.0.0.1:53682/callback`, and paste the Client ID and Secret into SET UP APP KEYS.
Then SIGN IN opens SoundCloud's own login page in your browser. After that: search tracks and
playlists, your playlists and your likes. Keys and login are stored encrypted for your Windows
account. Some tracks stream in a format that needs ffmpeg.

### Lyrics

SETTINGS → LYRICS shows time-synced lyrics for the playing song (Spotify, SoundCloud or your own
files) on the display, looked up on LRCLIB (lrclib.net), a free, open lyrics database.
Spotify's own lyrics aren't available to other apps.

## Retro Lens (photo & video editor)

`RetroLens/` makes photos and videos look like they came from old cameras: 80+ looks in sections by
style and by brand (Sony, Canon, Nikon, Kodak, Fujifilm, Olympus, Panasonic, JVC, Nokia, Motorola,
Apple, Samsung, Nintendo, Polaroid, Lomography, Microsoft…), 40+ settings (fisheye, pixelation,
palettes and dithering, JPEG, VHS bleed/jitter/tracking, CRT, grain, dust, light leaks, date stamps,
camcorder overlays, borders), your own presets, before/after compare, and PNG/JPG/MP4/GIF export
(videos need ffmpeg). The looks are approximations inspired by those devices.

## Retro Dash (car gauges for your PC)

`RetroDash/` is a car instrument cluster driven by your PC: the tachometer is CPU load, the
speedometer is network speed, fuel is free memory, temperature is CPU load over the last minute,
the odometer counts network data (1 km per MB), the indicators blink while downloading/uploading,
and warning lamps light up for a maxed-out CPU, low memory or a low laptop battery. It also shows
the song playing in Retro Radio. Three styles (80s digital VFD, classic analog, modern digital),
day/night lighting, colors, km/h or mph, sizes, always-on-top and a demo drive. Right-click for the
menu; keys: S style, D demo, N day/night, U units, T on top, R reset trip.

## Launcher

`Launcher/` is **Retro Multy Program**: it installs, updates and opens all the programs from the
GitHub releases of github.com/ryxn3/retro-multy-program. Build the release zips with
`build-release.ps1`, then upload everything in `dist` to a GitHub release (tag like `v1.0.0`).

## Retro Radio for Mac

`Mac/` builds Retro Radio for Apple Silicon Macs from the same source files. The Windows-only parts
it uses are replaced by small look-alikes in `Mac/Compat`: System.Drawing on SkiaSharp,
WinForms on Avalonia, NAudio output on PortAudio (Core Audio), MP3 via NLayer, other formats via
macOS's built-in `afconvert`, and DPAPI with AES-GCM. `build-release.ps1` publishes it for
`osx-arm64` and `Mac/Packager` wraps it as `Retro Radio.app` in `RetroRadio-macOS-arm64.zip`.
The app isn't signed with an Apple developer ID, so the first time run
`xattr -dr com.apple.quarantine "/Applications/Retro Radio.app"` (also in the zip's HOW TO OPEN.txt).
On a Mac the visualizer can't hear Spotify's audio, so it animates by itself while Spotify plays.

## Retro Radio for Android

`Android/` builds Retro Radio for Android phones and tablets (Android 8 or newer) from the same source
files, the same way as the Mac version: System.Drawing comes from the Mac port's SkiaSharp look-alike, and
`Android/Compat` has small Android stand-ins for WinForms and NAudio's output (sound through AudioTrack; MP3
via NLayer, FLAC/M4A/AAC/OGG/Opus via Android's own decoders). All radio models, visualizers, settings,
sounds, internet radio, SoundCloud/Spotify and lyrics are there.

- The radio fills the screen in landscape. Tap the keys; drag the VOLUME and SPEED knobs up and down.
- Menus and lists on the display (LIST, SETTINGS, EQ, SoundCloud): tap a row, **swipe** to scroll, tap twice
  to play, **hold** for a right-click (go back / step a setting the other way). The phone's **Back** is Esc.
- OPEN and FOLDER use the phone's file picker; the first time, the phone's Music folder is added
  automatically (after you allow access to music). You can also "Open with" Retro Radio from a file manager.
- The music keeps playing in the background (a notification shows while the radio is on). **POWER** turns
  it off; Back on the main screen just sends it to the background.
- Headset and car buttons (play/pause, next, previous) work; the phone's volume keys set the phone's volume.

Build it with the .NET 10 SDK and the Android workload (`dotnet workload install android`):

```
dotnet publish Android/RetroRadio.Android.csproj -c Release -f net10.0-android
```

The `.apk` lands in `Android/bin/Release/net10.0-android/publish/`. GitHub Actions
(`.github/workflows/android.yml`) builds `RetroRadio-android.apk` on every push that touches the radio,
keeps it as a download on the run's page, and attaches it to every published release. To install it, copy
it to the phone, open it and allow installing from that app. Like on a Mac, the visualizer can't hear
Spotify's audio (Android apps can't listen to other apps), so it animates by itself while Spotify plays.

## Website

`docs/` is the download site (hosted with GitHub Pages). On GitHub: **Settings → Pages → Build and
deployment → Deploy from a branch → `main` / `/docs`**. It links straight to the latest release's
`RetroLauncher-win-x64.zip`, so it works as soon as a release is published. Preview it locally
with `python -m http.server 8765 --directory docs`.
