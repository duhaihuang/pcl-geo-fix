# PCL-Unlock

PCL-Unlock is an independent region-detection compatibility helper for PCL Community Edition.

Some Windows installations expose incomplete or inconsistent regional metadata. As a result, a device located outside Mainland China may be incorrectly classified as being inside Mainland China, causing game-management features to become unavailable.

This helper is intended only for users outside Mainland China who are affected by that false-positive detection. It starts an existing PCL installation with a temporary compatibility context and does not permanently change Windows regional settings, replace the original launcher, modify the PCL executable on disk, or distribute any official PCL files.

## Requirements

- Windows x64
- .NET 10 Desktop Runtime
- A separately obtained copy of PCL Community Edition

## Usage

Download the official x64 PCL Community Edition release and place
`PCL-Unlock.exe` in the same directory as `PCL2_CE_Release_x64.exe`, then run:

```powershell
.\PCL-Unlock.exe
```

To select the launcher explicitly:

```powershell
.\PCL-Unlock.exe --pcl "C:\path\to\a-custom-pcl-name.exe"
```

To perform a compatibility check without opening the launcher:

```powershell
.\PCL-Unlock.exe --verify-only --pcl "C:\path\to\a-custom-pcl-name.exe"
```

Additional launcher arguments can be passed after `--`.

## Building

```powershell
dotnet publish .\PCL.Unblock.Launcher\PCL.Unblock.Launcher.csproj `
  -c Release `
  -o .\artifacts\win-x64
```

## Notes

- This project is intended to correct false-positive region detection on devices located outside Mainland China. It is not intended to alter feature availability for users located in Mainland China.
- Compatibility changes apply only to the PCL process started by this helper.
- Starting PCL directly preserves its original behavior.
- An unsupported PCL build will be stopped instead of continuing in an uncertain state.
- This project is not affiliated with or endorsed by the PCL or PCL Community Edition maintainers.

Third-party notices are included in the executable and can be displayed with:

```powershell
.\PCL-Unlock.exe --licenses
```
