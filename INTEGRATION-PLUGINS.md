# Integration projects and installation repositories

Links verified on **2026-10-08** against project pages, the maintained Sea of Stars feed, Dalamud's official feed, and the projects' own custom feeds. The manager lists these integrations only when the selected sync exposes their corresponding callers.

In the integration popup, **Project** opens the project's page in your browser. **Copy repo URL** copies the installation feed below. Paste it in `/xlsettings > Experimental > Custom Plugin Repositories`, add it, enable it, and save. Then open `/xlplugins > All Plugins > All` and search for the plugin, or use **Find** in the popup. Adding a repository makes plugins available; it does not install them automatically.

| Integration | Project / documentation | Installation repository |
| --- | --- | --- |
| Penumbra (required) | [xivdev/Penumbra](https://github.com/xivdev/Penumbra) | [Sea of Stars][sea] |
| Glamourer (required) | [Ottermandias/Glamourer](https://github.com/Ottermandias/Glamourer) | [Sea of Stars][sea] |
| Simple Heels | [Caraxi/SimpleHeels](https://github.com/Caraxi/SimpleHeels) | [Sea of Stars][sea] |
| Customize+ | [Aether-Tools/CustomizePlus](https://github.com/Aether-Tools/CustomizePlus) | [Sea of Stars][sea] |
| Honorific | [Caraxi/Honorific](https://github.com/Caraxi/Honorific) | Dalamud official repository; no custom URL needed |
| Moodles | [kawaii/Moodles](https://github.com/kawaii/Moodles) | [Sea of Stars][sea] |
| Pet Nicknames | [Glyceri/FFXIVPetRenamer](https://github.com/Glyceri/FFXIVPetRenamer) | Dalamud official repository; no custom URL needed |
| Brio | [Etheirys/Brio](https://github.com/Etheirys/Brio) | [Sea of Stars][sea] |
| Pulsar | [Drovolon/Pulsar](https://github.com/Drovolon/Pulsar), [user guide](https://pulsar.drovolon.org/) | [Pulsar feed](https://raw.githubusercontent.com/Drovolon/Pulsar/repo/repo.json) |
| LivePose (Simple Heels) | [Caraxi/SimpleHeels](https://github.com/Caraxi/SimpleHeels) | [Sea of Stars][sea]; LivePose is a Simple Heels feature, not a separate install |
| Lifestream | [NightmareXIV/Lifestream](https://github.com/NightmareXIV/Lifestream) | [NightmareXIV feed](https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json) |
| Stagehand | [universalconquistador/Stagehand](https://github.com/universalconquistador/Stagehand) | [Stagehand feed](https://github.com/universalconquistador/Stagehand/releases/latest/download/repo.json) |
| Intoner | [Abelfreyja/Intoner](https://github.com/Abelfreyja/Intoner) | [Intoner feed](https://raw.githubusercontent.com/Abelfreyja/Intoner/repo/repo.json) |
| Loci | [CordeliaMist/Loci](https://github.com/CordeliaMist/Loci) | [Loci feed](https://raw.githubusercontent.com/CordeliaMist/Loci/main/repo.json) |

[sea]: https://raw.githubusercontent.com/Ottermandias/SeaOfStars/main/repo.json

[Sea of Stars](https://github.com/Ottermandias/SeaOfStars) is a collection authorized by the contained projects. Add its URL once to access Penumbra, Glamourer, Customize+, Simple Heels, Brio, and Moodles; select which plugins to install yourself.

PlayerSync's **Moodles / Loci** row links to both projects separately. Use one at a time, as required by PlayerSync. A ready status does not establish conversion between their status-effect formats.

Pulsar shares music files and playback position through Lightless. Intoner shares saved furniture, object, VFX, and lighting layouts through Lightless. These remain optional; normal character appearance syncing does not require installing them. See [SYNC-PLUGIN-COMPARISON.md](SYNC-PLUGIN-COMPARISON.md) for service support and priority tradeoffs.

The code's corresponding link definitions are in [IntegrationDiagnostics.cs](src/Integrations/IntegrationDiagnostics.cs). Project and installation addresses can change; revisit these sources when updating the catalog.
