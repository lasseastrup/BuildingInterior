# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package adheres to [Semantic Versioning](https://semver.org/) as Unity defines it (MAJOR = breaking public API or data format, MINOR = additions, PATCH = fixes). Data format version: see `StoreyVersion.DataFormat`.

## [Unreleased]

### Added
- Facade details: AC units, vents, dishes, fire escapes (climbing through flush setbacks, with drop ladders) and awnings, hand-placed or by the style's seeded rules; `DetailRules` on `FacadeStyle` (workstream 3).
- Pitched roofs: straight skeleton, hip/gable/shed with eaves, fascias, soffits and gable walls; roofed setbacks cut around the storeys above (`Generate/Skeleton`, `Generate/Roofs`) (workstream 3).
- LOD0 generator core (`Generate`): slabs with stair holes and terrace decks, wall panels with openings, frames and panes, floor bands and plinths, interior walls with doors, party walls, setbacks with terraces and overhangs, flat roofs with parapets, stair and lift cores sharing building walls; `Validate.CoplanarCheck`; Clipper2 1.5.4 and Earcut 3.0.1 vendored (workstream 2).
- Data model (`BuildingData` and friends, SPEC §3), `PrototypeJson` read/write of the prototype's layout with unknown-key reporting, `Derived` (floor bases, tiers, outlines, styles, shaft tops), `DocumentSummary`, an owned JSON reader/writer, `StoreyDocumentAsset`, `StoreyQualitySettings` (workstream 1).
- Package skeleton: assembly definitions, `StoreyVersion`, smoke tests (workstream 0).
