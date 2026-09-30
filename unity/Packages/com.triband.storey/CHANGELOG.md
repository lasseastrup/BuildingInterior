# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package adheres to [Semantic Versioning](https://semver.org/) as Unity defines it (MAJOR = breaking public API or data format, MINOR = additions, PATCH = fixes). Data format version: see `StoreyVersion.DataFormat`.

## [Unreleased]

### Added
- Data model (`BuildingData` and friends, SPEC §3), `PrototypeJson` read/write of the prototype's layout with unknown-key reporting, `Derived` (floor bases, tiers, outlines, styles, shaft tops), `DocumentSummary`, an owned JSON reader/writer, `StoreyDocumentAsset`, `StoreyQualitySettings` (workstream 1).
- Package skeleton: assembly definitions, `StoreyVersion`, smoke tests (workstream 0).
