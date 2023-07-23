# Unity Splash Screen Remover

## Table of Contents

- [Overview](#overview)
- [Requirements](#requirements)
- [Usage](#usage)
- [Supported Platforms](#supported-platforms)
- [Known Supported Unity Versions](#known-supported-unity-versions)
- [Todo List](#todo-list)
- [Credits](#credits)
- [License](#license)
- [Disclaimer](#disclaimer)
- [FAQs](#faqs)

## Overview

The Unity Splash Screen Remover is a Command-Line Interface (CLI) tool designed to remove the Unity splash screen logo from Unity-built games. The tool is an implementation of the guide available at [https://github.com/kiraio-moe/remove-unity-splash-screen](https://github.com/kiraio-moe/remove-unity-splash-screen). By utilizing this tool, you can easily remove the Unity splash screen from your games.

## Requirements

- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0 ".NET 6.0 SDK")

## Usage

To use the tool, follow the steps below:

1. Download the Unity Splash Screen Remover from [Releases](https://github.com/kiraio-moe/USSR/releases).
2. Drag and drop your game executable to `USSR.exe` or execute USSR as follow:

    ```bash
    USSR.exe <your_game.exe>
    ```

## Supported Platforms

Unity Splash Screen Remover currently supports the following platforms:

- PC, Mac, Linux Standalone (Default Compression)

## Known Supported Unity Versions

- 2020
- 2019

For other versions, please test it yourself and let me know!

## Todo List

- Support WebGL platform.
- Support compressed build.

## Credits

Special thanks to [nesrak1](https://github.com/nesrak1) for the AssetsTools.NET library, which was instrumental in the development of this tool.

## License

This project is licensed under GNU GPL 3.0.

For more information about the GNU General Public License version 3.0 (GNU GPL 3.0), please refer to the official GNU website: <https://www.gnu.org/licenses/gpl-3.0.html>

## Disclaimer

By using this tool, you're intentionally violates the Unity End User License Agreement (EULA). Use the tool at your own risk (DWYOR - Do What You Own Risk).

## FAQs

**Q: Is using this tool safe?**  
A: Yes, the tool is designed to safely remove the Unity splash screen from your game without causing any harm.

**Q: Can I upload my game to game stores and not get banned?**  
A: Yes, you can upload your games to various game stores without facing any bans. However, it's important to note that by removing the Unity splash screen, you are violating the Unity EULA, and there is always a risk of potential consequences. Be aware of the risks before proceeding.

---

Please note that using this tool is at your own discretion and responsibility. Always make sure to backup your game files before using any third-party tools or modifying game assets.
