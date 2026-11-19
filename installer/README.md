# MSI/MSIX packaging checklist

- Publish self-contained `win-x64` build from `src/HydroNet.Client`.
- Set `RestartRecovery=true` and preserve `ScreenStateStore` in `%ProgramData%\HydroNet.Control`.
- Create desktop/start-menu shortcuts and run `/quiet` in the deployment pipeline.
- Verify a clean Windows VM: install, launch, reboot, confirm two-monitor layout recovery, uninstall.
