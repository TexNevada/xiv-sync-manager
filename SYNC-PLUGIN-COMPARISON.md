# Sync plugin comparison and default priority

Reviewed: **2026-10-07**.

XIV Sync Manager defaults to **PlayerSync > Lightless Sync > Snowcloak** for new configurations. This is a starting policy for everyday user experience: useful features remain available, loading can be controlled, and supported protections reduce particular crash and performance risks. It is not a measured ranking of server speed, uptime, or crash rates.

## Players retain the choice

- Use the priority arrows in `/syncmanager` to change the global order.
- Choose a preferred sync for an individual character to override the global order when that route is eligible.
- Both choices are saved across logouts and reloads and survive cache clearing.
- Updating the manager preserves an existing saved order, including the previous default. It does not force existing players onto the new recommendation.
- Missing services in a saved list are appended in the default order without reordering the saved entries. An empty or invalid list is rebuilt from the default.

Priority is used when automatic management is enabled, which starts **Off**. It chooses among eligible routes for duplicate characters; it does not disconnect every lower-priority service or suppress every character who uses only one service. Existing external pauses, local holds, manual choices, and retained fallback selections affect eligibility.

## Why this order

### 1. PlayerSync: default first choice

PlayerSync combines selectable server-compressed textures, default-on checks for specific broken files, animation crash guards, and controls for download and appearance-loading concurrency.

Its profile behavior is a deciding factor for this manager. PlayerSync hides profiles when either side pauses the pair. Our current integration suppresses its duplicate routes through server pair pauses, so selecting another service can make PlayerSync profiles unavailable. Keeping PlayerSync selected avoids the manager introducing that pause. It cannot override a pause set by the other player, native automatic pauses, or profile visibility restrictions.

Server-compressed texture alternatives can reduce download size, storage, and texture memory without requiring the receiving computer to perform that compression locally. Players can request original quality globally or for selected UIDs. Compression can change visual quality; the preferred mode is a user choice.

This recommendation assumes sensible performance settings. PlayerSync's concurrent pair download/application limit defaults to **unlimited**, and its threshold-based automatic pausing defaults to **Off**. A finite application limit and appropriate VRAM/triangle thresholds can improve the experience on constrained systems. The manager does not change these native settings.

### 2. Lightless Sync: second choice and a strong alternative

Lightless has extensive loading and optimization controls: prioritized downloads for direct/preferred pairs, separate processing workers, a pair download/application limiter enabled by default, optional texture optimization, and optional model simplification.

Its documented reconciliation system handles incremental updates, reconnects, stale operations, and retries for individual appearance fields. These are useful design properties, although source inspection alone cannot establish superior runtime reliability.

Lightless is a reasonable first choice when its extra integrations matter, including Pulsar, LivePose, or Intoner layout sharing, or when a player wants more control over local texture and model optimization. Those optimizations can consume local processing time and change visual quality. Most of the optional texture/model transformations default to **Off**.

### 3. Snowcloak: third choice, with particular strengths in crowds

Snowcloak provides aggregate crowd budgets based on visible syncshell members, estimated VRAM, and triangles. Its crowd controller prioritizes relationships when deciding which appearances to hold. It also offers per-player resource limits, local texture shrinking, appearance allow/block lists, and panic mode.

Its local performance holds can clear when an appearance falls within limits. The manager can also hold its downloads and appearance application locally, using a dedicated source, without setting a bidirectional server pair pause. Local appearance holds are separate from profile access, although the manager still needs a usable character identity to open a character's profile.

For a player frequently visiting packed venues or struggling with total crowd load, Snowcloak can be the better first choice. Its third position here reflects the combined profile and feature tradeoffs, not evidence that Snowcloak is slower or less reliable.

## Feature comparison

All three offer the Penumbra/Glamourer appearance workflow and integrations such as Customize+, Simple Heels, Honorific, Moodles, and pet names. The table focuses on differences relevant to choosing a route. A feature not found during this review is not proof that the project has no protection against that problem.

| Area | PlayerSync | Lightless Sync | Snowcloak |
| --- | --- | --- | --- |
| Animation crash guards | Guards against a null skeleton mapper during redraws; fixes some animation bone-mapping problems. | Runtime skeleton compatibility guards; optional incoming animation validation or repair. | No equivalent dedicated animation guard found in the inspected client source. |
| Broken-file handling | Default-on checks for specific known problems in animation timelines, VFX, and references to uninstalled expansion assets; excludes files identified as crash risks. | Incoming animation validation/repair setting defaults to Off; optional blocking of a specific problematic shader. | File integrity checks and resource limits; no equivalent timeline/VFX crash filter found. |
| Texture memory reduction | Server-compressed alternatives; original/compressed modes and per-UID original-quality overrides. Current code defaults to compressed alternatives. | Optional local compression, resolution/mip reduction, and server-provided optimized texture payloads where supported. | Optional local shrinking, generally to at most 2048 x 2048 for compressed textures or 1024 x 1024 for uncompressed textures. |
| Heavy models | Optional triangle-threshold pauses. | Optional triangle-threshold pauses and model simplification. | Local triangle-threshold blocking. |
| Per-player resource limits | Warnings and optional VRAM/triangle auto-pausing; auto-pausing defaults to Off. | Warnings and optional VRAM/triangle auto-pausing; auto-pausing defaults to Off. | Resource blocking defaults to On, with direct-pair exemptions and allow lists. |
| Aggregate crowd budgets | No equivalent aggregate controller found. | No equivalent aggregate controller found. | Visible-member, VRAM, and triangle budgets for syncshell crowds; crowd priority enabled by default in inspected source. |
| Parallel downloads | Normal slider 1-30; default 15. Upload concurrency and download bandwidth controls. | 1-64; hardware-based recommendations, upload/download bandwidth controls, and direct/preferred-pair download priority. | 1-10; default 10, with download bandwidth control. |
| Loading spikes | Separate concurrent pair download/application limit; defaults to unlimited. | Separate processing workers and combined pair download/application limiter; limiter defaults to On. | Separate decompression limit, automatic 1-2 workers or configurable up to 4. |
| Recovery from performance restrictions | Timed automatic pauses; default duration is indefinite. | Timers, temporary exemptions, unpause grace periods, and pause history. | Local resource blocks can clear when usage falls within limits; crowd holds reevaluated automatically. |
| Profiles on suppressed routes | Hidden when either side pauses the pair; keeping this route selected avoids the manager causing that pause. | No equivalent unconditional pause gate found; native visibility rules still apply. | Local holds are separate from profile access; character identity and native visibility rules still apply. |
| Nearby pairing/discovery | Opt-in ZoneSync joins zone-based syncshells automatically; broadcast syncshell discovery. | Lightfinder nearby pairing and syncshell discovery features. | Frostbrand nearby pair requests; community, venue, and roleplay features. |
| Distinctive companion features | Stagehand and Loci support; Moodles/Loci are alternatives. | Pulsar, LivePose, and Intoner layout sharing. | Public companion-plugin IPC with permissions and extension-data sharing; chat and roleplay features. |

More parallel downloads do not necessarily produce a smoother game. Downloads, decompression, texture processing, and appearance application consume different resources. Higher concurrency can shorten loading while increasing CPU, disk, memory, or frame-time pressure. Slider maxima are controls, not comparative speed measurements.

Transport compression and texture optimization also differ: lossless compression of a download stream saves bandwidth but does not by itself reduce the texture's GPU memory use. Texture compression, downscaling, and model simplification can reduce rendering costs and may affect appearance quality.

## How the manager affects these tradeoffs

### Some guards remain active on loaded backups

PlayerSync's animation bind guard and Lightless's skeleton guards are registered as plugin services. Their game hooks operate while the plugin is loaded and successfully initialized, rather than only for pairs selected by this manager. Selecting a different highest-priority route does not inherently remove those guards. Game updates or initialization failures can prevent a hook from becoming active.

File filtering and resource limits still matter for the route that actually downloads and applies the character. No inspected guard is a guarantee against every crash.

### Fallback can bypass a service's resource block

The current manager excludes externally paused or held routes and can choose another eligible service for that character. If Snowcloak blocks an oversized appearance, for example, another service could load it under different native limits.

Use compatible protection settings across services. A possible future improvement is to distinguish a native safety block from a connection failure and avoid automatically routing around that block; that policy is not implemented by this default-order change.

### Bidirectional pauses require compatible choices

Our Lightless and PlayerSync integrations use server pair pauses, which can stop syncing in both directions. If two players select different routes and pause each other's preferred service, there may be no common active route. Agreeing on a shared preferred service can matter more than small performance differences. Snowcloak's supported local holds avoid changing the remote pair's server pause.

## Evidence and limits

This review combined public documentation, public client source, the manager's integration code, and checks against installed plugin assemblies. Lightless 3.3.0.0 and PlayerSync 1.15.5.9 were inspected. Snowcloak's current public source/release was 4.3.0.4; the installed version was 4.2.4.0. The installed Snowcloak assembly also exposed crowd budgets, automatic resource blocking, texture shrinking, parallel download/decompression controls, and panic mode. Exact settings and behavior can change between releases.

Native defaults described here are source defaults, not a claim about a player's saved configuration. Existing settings, migrations, exemptions, server capabilities, and the other player's permissions can change the result.

There are no controlled comparative measurements of crash rates, frame times, server uptime, or download latency in this review. Regional CDN availability and server-side optimizations are capabilities, not proof that one service is always fastest. To reassess the default, compare the same characters, location, texture quality, and protection settings with cold and warm caches, including appearance updates and disconnect/reconnect recovery.

## Sources

- [PlayerSync client source](https://github.com/universalconquistador/MareSynchronosClient): protection, concurrency, companion integration, and profile behavior.
- [PlayerSync animation bind guard](https://github.com/universalconquistador/MareSynchronosClient/blob/main/PlayerSync/Interop/AnimationBindGuard.cs) and [skeleton mapping fix](https://github.com/universalconquistador/MareSynchronosClient/blob/main/PlayerSync/Interop/SkeletonMappingFix.cs).
- [PlayerSync file validation](https://github.com/universalconquistador/MareSynchronosClient/blob/main/PlayerSync.Validation/FileValidation.cs), [performance defaults](https://github.com/universalconquistador/MareSynchronosClient/blob/main/PlayerSync/MareConfiguration/Configurations/PlayerPerformanceConfig.cs), and [transfer controls](https://github.com/universalconquistador/MareSynchronosClient/blob/main/PlayerSync/UI/SettingsUi.Transfers.cs).
- [PlayerSync performance guide](https://docs.playersync.io/docs/features?open=performance-auto-pausing) and [service website](https://www.playersync.io/): selectable texture alternatives and advertised regional file delivery.
- [Lightless client source](https://git.lightless-sync.org/Lightless-Sync/LightlessClient) and [design document](https://git.lightless-sync.org/Lightless-Sync/LightlessClient/src/branch/master/DESIGN.md).
- [Lightless transfer controls](https://git.lightless-sync.org/Lightless-Sync/LightlessClient/src/branch/master/LightlessSync/UI/Settings/Tabs/TransfersSettingsTab.cs), [performance defaults](https://git.lightless-sync.org/Lightless-Sync/LightlessClient/src/branch/master/LightlessSync/LightlessConfiguration/Configurations/PlayerPerformanceConfig.cs), and [skeleton guards](https://git.lightless-sync.org/Lightless-Sync/LightlessClient/src/branch/master/LightlessSync/Services/Animation/SkeletonMappingGuardService.cs).
- [Snowcloak client source](https://github.com/Eauldane/SnowcloakClient), [crowd controller](https://github.com/Eauldane/SnowcloakClient/blob/main/Snowcloak/Services/Performance/CrowdPriorityController.cs), and [performance defaults](https://github.com/Eauldane/SnowcloakClient/blob/main/Snowcloak/Configuration/Configurations/PlayerPerformanceConfig.cs).
- [Snowcloak performance guide](https://docs.snowcloak-sync.com/snowcloak-user-guide/settings-and-safety/), [pair permissions](https://docs.snowcloak-sync.com/snowcloak-user-guide/pairs-and-permissions/), and [companion integrations](https://docs.snowcloak-sync.com/snowcloak-user-guide/plugin-integrations/).
- Local implementation: [configuration](src/Configuration.cs), [loading and saved-order repair](src/Plugin.cs), [selection rules](src/SyncModels.cs), and [pause/profile integration](src/Integrations/ReflectionSyncAdapter.cs).
