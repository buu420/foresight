Foresight Crash Collector 1.0.0

1. Extract all three files from this ZIP into a folder.
2. After a crash, BEFORE restarting the game, run "Collect Foresight Crash Logs.cmd".
3. Send back the new Foresight-Crash-Report ZIP in Desktop\Foresight Diagnostics,
   with the game location, action and approximate crash time.

The window stays open and prints the exact ZIP path. Collection does not start
or stop the game. Starting the game replaces bootstrap/proxy logs, so collect
first. report.json flags a running game and compares log modification times;
timestamps alone cannot prove that logs belong to the same session.
It reads the three newest raw Chrono Trigger Reloaded logs,
bootstrap.log, proxy.log, and matching Windows Application crash entries from
the last three days. Existing logs are left unchanged. Missing or unreadable
sources are listed in report.json. The unpacked report is kept next to its ZIP.

Steam libraries and a game folder containing this utility are detected
automatically. If the game folder is not found, gameplay logs and Windows
events can still be collected, but bootstrap/proxy logs will be marked missing.
For a custom location, open PowerShell in this folder and run:

.\Collect-Foresight-CrashReport.ps1 -GameRoot "D:\SteamLibrary\steamapps\common\Chrono Trigger"

Optional parameters:
-ReloadedLogDirectory "path"  Custom raw Reloaded log directory.
-OutputDirectory "path"      Write reports somewhere other than the Desktop.
-Days 7                     Look back up to 14 days for Windows crash entries.
-GameplayLogCount 5          Collect up to 10 recent Chrono Trigger sessions.
-SkipWindowsEvents          Collect logs only, recording that events were skipped.

The ZIP may contain local paths, usernames, computer name and gameplay text.
It contains no saves, settings, recordings, game data or executable, and it
does not transmit files. Your existing sync/backup software may copy the output
folder, including a Desktop synced by OneDrive. This utility collects diagnostics;
the game crash still requires diagnosis.
