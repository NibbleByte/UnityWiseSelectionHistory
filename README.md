<img src="https://raw.githubusercontent.com/NibbleByte/UnityWiseSelectionHistory/refs/heads/master/Docs/PublishImages/Icon-160.png" width="160" align="right">

# Wise Selection History
Selection history that works with mouse back and forward buttons.

## Features
Use mouse back and forward button to navigate the selection history or open the selection history window via "Edit / Selection / History Window"
* Horizontal and vertical layout support.
* Mouse Back / Forward buttons step through the selection history.
* Drag entries into scene, project or inspector as if dragging the real objects.
* Drag entries to reorder.
* Pin entries for quick access with right-click context menu.
* Scene objects are tinted in blue. Changing scene clears them.
* Destroyed entries are shown as "(missing)".
* Copy names or paths with right-click context menu.
![Wise Selection History screenshot](https://raw.githubusercontent.com/NibbleByte/UnityWiseSelectionHistory/refs/heads/master/Docs/Screenshots/MainWindowShot.png)

## Installation
* [Asset Store](https://u3d.as/4ht5)
* [OpenUPM](https://openupm.com/packages/devlocker.tools.wiseselectionhistory) support:
```
npm install -g openupm-cli
openupm add devlocker.tools.wiseselectionhistory
```
[![openupm](https://img.shields.io/npm/v/devlocker.tools.wiseselectionhistory?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/devlocker.tools.wiseselectionhistory/)

* GitHub UPM package - merge this into your `Packages/manifest.json`
```
{
  "dependencies": {
    "devlocker.tools.wiseselectionhistory": "https://github.com/NibbleByte/UnityWiseSelectionHistory.git#upm"
}
```

## Notes
- On **Windows**, the mouse buttons are read directly, so they work in any editor window while Unity is the focused application.
- On **macOS and Linux**, the mouse buttons only work over the Scene view, the Hierarchy and the Project window.
- Pins are stored in `UserSettings/WiseSelectionHistoryPins.asset`.

## License
MIT. See [LICENSE](https://github.com/NibbleByte/UnityWiseSelectionHistory/blob/master/LICENSE).
