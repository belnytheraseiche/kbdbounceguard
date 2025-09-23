# KBD BOUNCE GUARD

A utility to prevent keyboard chattering (bouncing).

## System Requirements

  * **OS:** Windows 10, Windows 11
  * **Architecture:** x64, ARM64

## Usage

### Startup

Run `kbdbounceguard.exe`. The application will run in the system tray.

### Shutdown

Right-click the `KBD BOUNCE GUARD` icon in the system tray and select "Quit" from the context menu.

## Configuration (config.json)

You can adjust the chattering prevention behavior by editing the `config.json` file located in the same directory as the executable.

| Parameter | Type | Description |
| :--- | :--- | :--- |
| `chatter_threshold` | Number | **The time window in milliseconds to detect chattering.**<br>If the same key is pressed again within this time, subsequent presses will be ignored. |
| `repeat_threshold` | Number | **The time window in milliseconds to detect a key repeat.**<br>If `ignore_repeat` is `true`, key presses with an interval longer than this value are considered legitimate key repeats and are not suppressed. |
| `updown_threshold` | Number | **The threshold in milliseconds for key press/release duration.**<br>Keystrokes with a down-to-up duration shorter than this value may be adjusted as part of chattering suppression. |
| `ignore_repeat` | Boolean | **Whether to ignore OS-level key repeats.**<br>If set to `true`, legitimate key repeats will not be treated as chattering. |
| `keyup_chatter` | Boolean | **Whether to suppress chattering that can occur on key release.**<br>Set to `true` if you experience unintentional key presses when you lift your finger off a key. |
| `allow_ime_ctrl_backspace` | Boolean | **Whether to allow the 'Ctrl + Backspace' shortcut in IMEs.**<br>Set to `true` to prevent this shortcut from being incorrectly identified as chattering, which can be useful when deleting words in East Asian languages. |

## Disclaimer

  * Use this software at your own risk.
  * The author generally provides no support and is not obligated to address any issues or bugs.
  * You are free to modify and redistribute this software as you wish.

## Copyright

[https://github.com/belnytheraseiche/kbdbounceguard](https://github.com/belnytheraseiche/kbdbounceguard)
