# Spark 3.2.2

## New

### Music tab: EchoVRMusic (replaces Echo Speaker System)
Your PC's music, playing out of Echo VR's own in-game speakers.

- **Real in-game speakers.** Audio goes through Echo's own sound engine, from speakers placed around each map. No virtual audio cable and no API access needed.
- **One-click install.** Open the Music tab and press **Install EchoVRMusic**. It installs into the Echo VR folder Spark already knows, and sets up the `plugins` folder and the plugin loader for you. It tells you if Echo VR is still running.
- **Settings right in the tab, applied in game instantly:**
  - volume
  - "Silent beyond" (cut-off distance)
  - full-volume radius
  - nearest speakers only
  - stereo
  - pause Echo's own music
  - all of Windows or one app as the source
- **Speakers built in** for the Arena, Dyson, Surge, Combustion and Fission. The lobby uses its own speakers, and on other maps the music plays beside you.
- **Place your own speakers** in game: stand where you want one and press **1**, and press **2** to remove the last one. **Ctrl+Alt+M** turns the music on and off.
- **Matches your Spark theme,** including live theme changes.

### Map testing invite
A one-time popup inviting PC players to the Echo VR map testing Discord, where you can install custom maps and play them online with others.

## Fixes
- The Quest IP search can no longer crash Spark when pings fail.
- Speed and velocity stats now use the API's measured frame rate instead of the target rate, so they stay correct when polling runs slower than requested.
- The API fetch loop no longer holds a background thread while it waits between frames.

## Removed
- **Echo Speaker System:** the separate Unity app, its installer, its update check, and the virtual audio cable setup. The **Speaker** tab is now **Music**, and its switch in Settings is renamed to match.

## Other
- README: new install link, the .NET version, and new Idle Dash and Combat Dash sections.
