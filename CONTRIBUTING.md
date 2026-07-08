# Contributing to Sandwich Station

Welcome! If you're considering contributing to Sandwich Station, [Wizard's Den's PR guidelines](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html) are a great starting point for code quality and version tracking etiquette. Note that we do not have the same master/stable branch distinction.

**Important:** Do not use GitHub's web editor to create PRs. PRs submitted through the web editor may be closed without review.

**"Upstream"** refers to the [HardLightSector/HardLight](https://github.com/HardLightSector/HardLight) repository that this fork was created from.

---

## Sandwich-Specific Content

Anything you create from scratch (vs. modifying something that exists from upstream) should go in a **Sandwich-specific subfolder**, `_Sandwich`.

### Examples:
- `Content.Server/_Sandwich/Shipyard/Systems/ShipyardSystem.cs`
- `Resources/Prototypes/_Sandwich/Loadouts/role_loadouts.yml`
- `Resources/Audio/_Sandwich/Voice/Goblin/goblin-scream-03.ogg`
- `Resources/Textures/_Sandwich/Tips/clippy.rsi/left.png`
- `Resources/Locale/en-US/_Sandwich/devices/pda.ftl`
- `Resources/ServerInfo/_Sandwich/Guidebook/Medical/Doc.xml`

# Changes to upstream files

If you make a change to an upstream C# or YAML file, **you must add comments on or around the changed lines**.
The comments should clarify what changed, to make conflict resolution simpler when a file is changed upstream.
If you make changes to values, to be consistent, leave a comment in the form `Sandwich: OLD<NEW`.

For YAML specifically, if you add a component or add a list of contiguous fields, use block comments, but if you make limited edits to a component's fields, comment the fields individually.

For C# files, if you are adding a lot of code, consider using a partial class when it makes sense.

If cherry-picking upstream features, it is best to comment with the PR number that was cherry-picked.

As an aside, fluent (.ftl) files **do not support comments on the same line** as a locale value - leave a comment on the line above if modifying values.

## Examples of comments in upstream or ported files

A single line comment on a changed yml field:
```yml
- type: entity
  id: TorsoHarpy
  name: "harpy torso"
  parent: [PartHarpy, BaseTorso] # Sandwich: add BaseTorso
```

A change to a value (note: `OLD<NEW`)
```yml
  - type: Gun
    fireRate: 4 # Sandwich: 3<4
    availableModes:
    - SemiAuto
```

A cyborg module with an added moduleId field (inline blank comment), a commented out bucket (inline blank comment), and a DroppableBorgModule that we've added (begin/end block comment).
```yml
  - type: ItemBorgModule
    moduleId: Gardening # Sandwich
    items:
    - HydroponicsToolMiniHoe
    - HydroponicsToolSpade
    - HydroponicsToolClippers
    # - Bucket # Sandwich
  # Sandwich: droppable borg items
  - type: DroppableBorgModule
    moduleId: Gardening
    items:
    - id: Bucket
      whitelist:
        tags:
        - Bucket
  # End Sandwich
```

A comment on a new imported namespace:
```cs
using Content.Client._Sandwich.Emp.Overlays; // Sandwich
```

A pair of comments enclosing a block of added code:
```cs
component.Capacity = state.Capacity;

component.UIUpdateNeeded = true;

// Sandwich: ensure signature colour is consistent
if (TryComp<StampComponent>(uid, out var stamp))
{
    stamp.StampedColor = state.Color;
}
// End Sandwich
```

An edit to a Delta-V locale file, note the `OLD<NEW` format and the separate line for the comment.
```fluent
# Sandwich: "Job Whitelists"<"Role Whitelists"
player-panel-job-whitelists = Role Whitelists
```
## No SPDX Headers

Do **not** add SPDX headers (`SPDX-FileCopyrightText` or `SPDX-License-Identifier`) to any files in your PRs. This applies to both new files and modifications to existing ones.

The entire codebase is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0-or-later)** as stated in the `LICENSE` file. Per-file SPDX headers are unnecessary and create several problems:

- They imply individual copyright ownership on a per-file basis, which doesn't reflect how collaborative development works — a one-line change shouldn't grant copyright over a 300-line file.
- They bloat files with repeated metadata that duplicates what the repo-level `LICENSE` already covers.
- They create inconsistent attribution when files are ported or cherry-picked between forks.

# Mapping

For ship submissons, refer to the [Ship Submission Guidelines](https://frontierstation.wiki.gg/wiki/Ship_Submission_Guidelines) on the Frontier wiki.

In general:

Sandwich uses specific prototypes for points of interest and ship maps (e.g. to store spawn information, station spawn data, or ship price and categories).  For ships, these are stored in the VesselPrototype (Resources/Prototypes/_Sandwich/Shipyard) or PointOfInterestPrototype (Resources/Prototypes/_Sandwich/PointsOfInterest).  If creating a new ship or POI, refer to existing prototypes.

If you are making changes to a map, check with the map's maintainer (or if none, its author), and avoid having multiple open features with changes to the same map.

Conflicts with maps make PRs mutually exclusive so either your work on the maintainer's work will be lost, communicate to avoid this!

# Before you submit

Double-check your diff on GitHub before submitting: look for unintended commits or changes and remove accidental whitespace or line-ending changes.

Additionally, for PRs that've been open for a long time, if you see `RobustToolbox` in the changed files, you have to revert it. Use `git checkout SandwichStation/SandwichStation-HL RobustToolbox`

# Changelogs

Currently, all changelogs go to the Sandwich changelog.


## AI-Generated Content Policy

AI-generated **code is strictly prohibited**. However, AI-generated assets such as music or sprites may be submitted **with explicit permission** from the maintainers.

| Content Type | Allowed | Requirement |
|--------------|---------|-------------|
| Code | No | Never permitted |
| Music | Yes | Requires explicit permission |
| Sprites | Yes | Requires explicit permission |
| Other Assets | Depends | Requires explicit permission |

## Submitting AI-generated content without explicit permission may result in removal of the contribution or a ban from contributing to the repository.
