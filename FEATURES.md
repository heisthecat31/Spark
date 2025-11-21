# Spark - Comprehensive Feature List

## Overview
Spark is a Windows desktop application designed for EchoVR players, streamers, and spectators. It provides extensive data recording, processing, analysis, and streaming capabilities for the EchoVR game.

---

## Core Features

### 1. **Real-Time Game Data Capture**
- Fetches game state from EchoVR API at 30Hz or 15Hz (configurable)
- Captures session data, player positions, disc location, and game events
- Supports both PC and Oculus Quest clients on the same network
- Fetches player bone data for advanced replay analysis
- Automatic connection detection and reconnection
- Multi-instance support with port override

### 2. **Replay Recording & Management**
- **Live Replay Recording**: Continuous recording of match data in custom `.echoreplay` format
- **Automatic Match Detection**: Automatically starts/stops recording based on game state
- **Replay File Management**: Save, split, and compress replay files
- **MILK Format Support**: Advanced compression format (`.milk`) for smaller file sizes
- **Replay Clips**: Automatically save clips of specific events:
  - Goals (scorer and assister)
  - Saves
  - Interceptions
  - Jousts (neutral and defensive)
  - Emotes
  - Playspace abuse detection
- **Configurable Clip Settings**:
  - Seconds before/after event
  - Player scope (self only, team, or everyone)
  - Spectator-specific recording options
- **Live Replay Hosting**: Stream live replay data to IgniteVR database

### 3. **Event Detection & Analysis**
The application detects and logs numerous in-game events:

#### Player Events
- **Scoring Events**: Goals, assists, 2-point vs 3-point scoring
- **Possession Events**: Catches, throws, passes, turnovers, interceptions
- **Defensive Events**: Saves, steals, blocks, stuns
- **Movement Events**: Jousts (neutral, defensive), big boosts
- **Player Actions**: Emotes (left/right), playspace abuse detection
- **Team Events**: Player joins, leaves, team switches

#### Match Events
- Match start/end, round changes
- Game state transitions (pre-match, round start, playing, score, round over, post-match)
- Overtime detection
- Pause/unpause detection with nearest player identification
- Restart requests
- Match resets
- Rules changes

#### Advanced Analytics
- Throw analysis (left/right handed, underhandedness)
- Goal trajectory tracking
- Backboard detection
- Shot angle calculation
- Joust timing and speed measurements
- Server score calculation (ping-based quality metric)
- Player statistics accumulation per round

### 4. **NVIDIA Highlights Integration**
- Automatic highlight capture for key moments
- Configurable player scope (client only, team, all players)
- Customizable time before/after events
- Event-specific highlights:
  - Goals
  - Assists
  - Saves
  - Interceptions
  - Stuns
  - Big boosts
- Manual highlight triggering

### 5. **Broadcasting & Streaming Tools**

#### OBS Integration
- OBS WebSocket support
- Scene switching based on game events
- Automatic stream control
- Source visibility management

#### Medal.tv Integration
- Automatic clip creation and upload
- Event-based triggers

#### Overlay System
- Browser source overlays for OBS/streaming
- Real-time scoreboard display
- Custom overlay configurations
- WebSocket server for overlay communication (port 6724)
- Static and dynamic overlay support
- Svelte-based overlay framework

### 6. **Camera Control**
- **Camera Write System**: Write camera data to game for spectator view control
- **Camera Transforms**: Bezier curves, spline interpolation
- **Camera Settings**: FOV, position, rotation, smoothing
- **Follow Player Mode**: Automatic camera following for "Spectate Me" feature
- **Camera Controller**: Advanced camera manipulation for replays

### 7. **Quest Integration**
- **Quest IP Detection**: Automatic Quest device discovery on local network
- **Network Scanning**: Ping-based Quest location (ARP table inspection)
- **Quest Data Fetching**: Direct API access to Quest EchoVR client
- **PC-Quest Spectate**: Remote spectator streaming from Quest to PC

### 8. **Social & Communication Features**

#### Discord Integration
- **Rich Presence**: Show current game status, match type, team names
- **OAuth Authentication**: Discord login for personalized features
- **Team Name Display**: VRML team names in presence

#### Text-to-Speech (TTS)
- Customizable voice and speech rate
- Event announcements
- Player name pronunciation
- Localization support (English, Spanish, Japanese)

#### Speech Recognition
- Microphone input capture
- Speaker (system audio) capture
- Vosk-based speech recognition
- Automatic model download
- Bad word detection and filtering

### 9. **Atlas Link System**
- Custom URL scheme handler (`spark://`, `atlas://`, `ignitebot://`)
- Quick join links for matches
- Spectator/player mode selection
- Link format customization
- Automatic game launching with session joining
- Team name appending to links

### 10. **Database & Cloud Features**
- Local SQLite database for match history
- Tablet stats upload to IgniteVR servers
- Auto-upload profiles per player
- Server-side data synchronization
- Live replay streaming to cloud
- CCU (Concurrent Users) tracking
- Session data upload

---

## User Interface Features

### 11. **Main Live Window**
- Real-time match statistics display
- Team rosters with player stats
- Game clock and score
- Server information
- Connection status
- Ping display
- Match history

### 12. **Settings System**
- Unified settings window
- Tabbed interface for different feature categories
- Settings persistence (JSON-based)
- Import/export configurations
- EchoVR settings management
- Loading tips customization
- Default settings templates

### 13. **Overlay Configuration**
- Visual overlay editor
- Position and size customization
- Transparency controls
- Font and color selection
- Multiple overlay profiles
- Preview mode

### 14. **Additional Windows**
- **QuestIPs Window**: Quest device discovery and management
- **First Time Setup**: Onboarding wizard
- **Update Window**: Version checking and auto-update
- **Login Window**: Discord OAuth flow
- **Message Boxes**: Custom themed dialogs
- **Atlas Whitelist**: Manage allowed spectators
- **Create Server**: Private match creation with rules
- **Playspace Visualizer**: 3D playspace boundary viewer
- **Ping Graph**: WebView-based ping visualization
- **Lone Echo Subtitles**: Accessibility feature

---

## Advanced Features

### 15. **EchoVR Process Management**
- Automatic EchoVR path detection (Registry-based)
- Launch game with custom parameters:
  - Spectator mode
  - Combat mode
  - NoVR mode
  - Capture VP2
  - Custom lobby joining
  - Port specification
  - Region selection
- Process monitoring and auto-restart
- Crash detection with configurable auto-restart timer
- Window focus management
- Multiple instance support

### 16. **Speaker System Integration**
- Echo Speaker System installer
- Version checking and auto-update
- Process management
- Unity handle integration

### 17. **Global Hotkeys**
- System-wide keyboard shortcuts
- Customizable key combinations
- No-repeat modifier support
- Multi-hotkey support

### 18. **HID Device Support**
- 3Dconnexion SpaceMouse support
- Generic HID device input
- Camera control via 3D mouse
- Real-time position/rotation tracking

### 19. **Spectate Me System**
- Automatic spectator launching
- PC-to-PC spectator mode
- Quest-to-PC spectator streaming
- Follow player camera mode
- Session synchronization
- Multi-PC setup support

### 20. **Network Services**
- **WebSocket Server**: Real-time data streaming
- **ASP.NET Web Server**: REST API endpoints (port 6724)
- **NetMQ/ZMQ**: Pub/sub messaging system
- **HTTP Client**: API communication
- **CORS Support**: Cross-origin requests for overlays

### 21. **Data Export & APIs**
- `/api/session` - Current session data (JSON)
- `/api/player_bones` - Player skeleton data
- `/api/focus_spark` - Focus application window
- REST endpoints for match data
- WebSocket streaming for real-time updates
- Batch output formats

---

## Configuration & Customization

### 22. **Themes**
- Multiple color themes
- Dark mode support
- Customizable accent colors
- Orange-themed default

### 23. **Localization**
- Multi-language support:
  - English (default)
  - Spanish
  - Japanese
- Localized UI strings
- Localized loading tips
- Culture-aware formatting

### 24. **EchoVR Settings Management**
- Read/write game settings files
- Spectator settings templates
- Multiplayer settings templates
- Loading tips injection
- Settings backup and restore

### 25. **Logging System**
- Multi-level logging (Error, Info, File, Database)
- Session-based log files
- Console output
- File output
- Database logging
- Configurable verbosity

---

## Technical Features

### 26. **Performance Optimizations**
- Async/await patterns throughout
- Parallel data processing
- Frame interpolation
- Smooth delta time calculation
- Memory-efficient data structures
- Configurable polling rates (low frequency mode)

### 27. **Error Handling**
- Graceful degradation
- Connection retry logic
- Timeout handling
- Exception logging
- User-friendly error messages
- Crash recovery

### 28. **Security**
- Secret key management
- Hash-based authentication
- OAuth token refresh
- Secure credential storage

### 29. **Notifications**
- Windows 10/11 toast notifications
- System tray integration
- Custom notification icons
- Persistent tray icon

### 30. **Auto-Update System**
- Version checking via GitHub API
- Update notifications
- Download progress tracking
- Automatic installation option

---

## Ecosystem Integrations

### 31. **IgniteVR Cloud Services**
- Account authentication
- Match data upload
- Tablet stats synchronization
- Access code validation
- Server-side analytics

### 32. **GitHub Integration**
- Version checking
- Release notes fetching
- Echo Speaker System updates

### 33. **Third-Party APIs**
- EchoVR API (official game API)
- Discord Rich Presence API
- NVIDIA Highlights API
- OBS WebSocket protocol
- Medal.tv API

---

## Feature Analysis: Technology Requirements

### Features That REQUIRE C# / .NET

#### Must be C# Specifically:
1. **WPF User Interface**
   - Reason: WPF is a .NET-exclusive UI framework
   - Alternative: Could use Electron, Qt, or other cross-platform frameworks
   
2. **Windows Forms Integration**
   - Reason: Legacy Windows Forms components
   - Alternative: Could be rewritten with modern UI frameworks

3. **NVIDIA Highlights SDK**
   - Reason: Native DLL wrapper requires P/Invoke (though other languages can do this)
   - Alternative: Native code or other language FFI

4. **Windows API Calls**
   - Extensive P/Invoke for window management, hotkeys, registry access
   - Alternative: Native code or other languages with FFI support

5. **ASP.NET Core Web Server**
   - Reason: Built on .NET Core
   - Alternative: Node.js, Python Flask, Go, etc.

#### Benefits from .NET but Not Required:
1. **Async/Await Pattern** - Available in many modern languages
2. **LINQ Queries** - Could use functional programming in other languages
3. **JSON Serialization** (Newtonsoft.Json) - Available in all major languages
4. **SQLite Integration** - Cross-platform database

### Features That REQUIRE Windows Desktop

#### Absolutely Windows-Only:
1. **WPF Application** - Windows-only UI framework
2. **Windows Registry Access** - For EchoVR path detection, URL scheme registration
3. **Windows Notifications** - Uses Windows 10/11 notification system
4. **Global Hotkeys** - Uses Windows API for system-wide keyboard hooks
5. **Window Management** - Focus stealing, window manipulation via Win32 API
6. **Process Injection** - Speaker System integration requires Windows process APIs
7. **HID Device Management** - Windows HID API
8. **System Tray Icon** - Windows-specific notification area
9. **Windows Audio APIs** - NAudio for speech recognition uses Windows CoreAudio
10. **WebView2** - Microsoft Edge WebView component

#### Windows-Dependent but Could Be Abstracted:
1. **File System Paths** - Uses Windows special folders (AppData, etc.)
2. **Process Management** - Could work on other platforms with changes
3. **Network Stack** - Cross-platform but uses Windows-specific optimizations

### Features That REQUIRE a Local Agent on Windows

These features need an application running on the local Windows machine:

1. **Game Data Capture**
   - Reason: Must poll local EchoVR API (127.0.0.1:6721)
   - Cannot work remotely without VPN/tunneling

2. **Game Process Management**
   - Launch, kill, monitor EchoVR process
   - Requires local system access

3. **NVIDIA Highlights Recording**
   - Requires local NVIDIA GeForce Experience
   - Must run on gaming PC with NVIDIA GPU

4. **OBS Control**
   - OBS WebSocket connection typically local
   - Could work remotely but not ideal for latency

5. **Global Hotkeys**
   - Must capture system-wide keyboard input
   - Requires local keyboard hook

6. **Speech Recognition**
   - Captures local microphone and speaker audio
   - Requires local audio device access

7. **HID Device Input**
   - 3D mouse must be connected locally
   - USB device access required

8. **Camera Write**
   - Writes camera data to local game instance
   - Requires local EchoVR API access

9. **Quest IP Detection**
   - Scans local network for Quest devices
   - Requires network adapter access

10. **File Management**
    - Reads EchoVR settings files from local installation
    - Saves replay files to local storage

### Features That COULD Be Cloud-Based or Cross-Platform

1. **Data Analysis & Statistics**
   - Processing game data could happen anywhere
   - Current implementation: Local for low latency

2. **Replay File Processing**
   - MILK compression/decompression could be done server-side
   - Current implementation: Local for privacy and performance

3. **Overlay Serving**
   - Web server could run anywhere
   - Current implementation: Local for zero-latency streaming

4. **Database Storage**
   - Match history could be cloud-based
   - Current implementation: Local SQLite + cloud sync hybrid

5. **WebSocket Streaming**
   - Could stream to remote clients
   - Current implementation: Local network for performance

6. **Event Detection Logic**
   - Pure algorithms could run anywhere
   - Current implementation: Local for real-time processing

### Features That DON'T Require C# or Could Be Platform-Agnostic

1. **Overlay Frontend** - Already uses Svelte/TypeScript/JavaScript
2. **REST API Endpoints** - Could be implemented in any language
3. **WebSocket Server** - Available in all major languages
4. **Data Structures** - JSON-based, language-agnostic
5. **Event Detection Algorithms** - Pure logic, any language
6. **Replay File Format** - Binary format, parseable anywhere
7. **Server Score Calculation** - Pure math, any language
8. **Discord Rich Presence** - SDKs available for many languages
9. **HTTP Client** - Universal functionality

### Architecture Recommendations

#### If Porting to Other Platforms:

**macOS Version:**
- Replace WPF with: Avalonia UI, SwiftUI, or Electron
- Replace Registry with: plist files
- Replace Win32 APIs with: Cocoa APIs
- Game launching: Would need EchoVR Mac support (doesn't exist)
- Most data processing could remain similar

**Linux Version:**
- Replace WPF with: Avalonia UI, GTK, Qt, or Electron
- Replace Registry with: config files
- Replace Win32 APIs with: X11/Wayland APIs
- Game launching: Would need EchoVR Linux support (doesn't exist)
- Most data processing could remain similar

**Web-Based Version:**
- Frontend: Already has Svelte overlay
- Backend: Could port to Node.js, Python, Go, Rust
- Limitations: No game launching, no local file access, no hardware integration
- Could work as remote spectator/analyzer

**Mobile Version:**
- Could create viewer app (iOS/Android)
- Would need to connect to PC running full Spark
- No game control capabilities
- Good for remote monitoring

### Key Insight
The **core value** of Spark requires:
1. **Windows OS** - Because EchoVR only runs on Windows
2. **Local Agent** - Must run on the same machine (or network) as EchoVR
3. **C# is optional** - But well-suited due to WPF, async/await, and Windows API integration

The application could theoretically be rewritten in:
- **C++** with Qt or wxWidgets
- **Rust** with a GUI framework
- **Electron** for cross-platform desktop
- **Python** with PyQt or Tkinter

However, C# with .NET is optimal for this use case because:
- Excellent Windows integration
- Mature WPF framework
- Great async programming model
- Strong tooling (Visual Studio)
- Good performance for this workload
- Large ecosystem of libraries

### Summary Matrix

| Feature Category | Requires C# | Requires Windows | Requires Local Agent | Could Be Cloud/Remote |
|-----------------|-------------|------------------|---------------------|----------------------|
| Game Data Capture | ❌ | ✅ | ✅ | ❌ |
| WPF UI | ✅ | ✅ | ❌ | ❌ |
| Replay Recording | ❌ | ✅ | ✅ | Partial |
| Event Detection | ❌ | ❌ | ❌ | ✅ |
| NVIDIA Highlights | ❌ | ✅ | ✅ | ❌ |
| OBS Integration | ❌ | Preferred | Preferred | Partial |
| Overlay Server | ❌ | ❌ | Preferred | ✅ |
| Camera Control | ❌ | ✅ | ✅ | ❌ |
| Quest Integration | ❌ | ❌ | ✅ (Network) | ❌ |
| Discord Integration | ❌ | ❌ | Preferred | Partial |
| TTS | ❌ | ✅ | ✅ | ❌ |
| Speech Recognition | ❌ | ✅ | ✅ | ❌ |
| Global Hotkeys | ❌ | ✅ | ✅ | ❌ |
| Database | ❌ | ❌ | Preferred | ✅ |
| Web APIs | ❌ | ❌ | ❌ | ✅ |

**Legend:**
- ✅ = Required
- ❌ = Not Required
- Preferred = Works best locally but not strictly required
- Partial = Some aspects could be remote

