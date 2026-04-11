# Install

## Requirements

- Windows.
- Path of Exile 1.
- ExileApi.
- .NET 10 SDK only if you want to build BeastsV2 manually.

## Install With PluginUpdater

1. Open ExileApi.
2. Open the `PluginUpdater` plugin.
3. Click the `Add` tab.
4. Paste `https://github.com/Kiritocs/BeastsV2` into `Repository URL`.
5. Click `Clone`.
6. Either restart ExileApi, or open ExileApi `Core` settings, scroll down, and press `Reload Plugins`.

## Install From Source Folder

1. Download or clone this repository.
2. Place the `BeastsV2` folder inside your `Plugins/Source/` directory.
3. Launch ExileApi.
4. Let the host compile the plugin.
5. Enable `Beasts V2` in the plugin settings.
6. Follow the first-time setup flow in `README.md`.

## Build Manually

1. Open a terminal in the `BeastsV2` project folder.
2. Run `dotnet build .\BeastsV2.csproj`.
3. Confirm the build succeeds.
4. The compiled plugin DLL is written to `bin\Debug\net10.0-windows\BeastsV2.dll`.

## Layout Assumptions

- Runtime config and saved-session data stay under the host application's `config` folder.
- Saved analytics sessions are written to `config\BeastsV2Sessions`.

## Updating

1. Replace the existing `BeastsV2` source folder with the new release.
2. Keep your host `config\BeastsV2` settings folder if you want to preserve configuration.
3. Keep `config\BeastsV2Sessions` if you want to preserve saved analytics sessions.
4. Launch ExileApi again and let it rebuild, or use `Core -> Reload Plugins`.

## Troubleshooting

- If automation feels too fast after install, raise `Automation -> Timing -> Flat Extra Delay (ms)` in small steps.
- If stash or merchant selectors are empty, open the relevant in-game UI first and then reopen the plugin settings.