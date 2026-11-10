# GrabForge interface

## Direction

- Product: choose video and audio streams, process sound, and save local media.
- Character: quiet, precise, efficient.
- World: editing-room instruments; graphite surfaces with restrained violet selection and focus.
- Moments: the actual source thumbnail, readable format details, and the state of independent audio effects.
- Signature: aligned video/audio format tables with a fine marker on each selected stream.

## Audit and changes

The v0.2.2 screen mixed white system combo boxes with dark inputs, blue panels, violet tabs, and dense table borders. Labels used several unrelated blue-grey colors. Navigation and controls had inconsistent spacing, and empty format lists had no context.

The interface now uses one neutral ramp and three text roles. The main action is off-white with dark text. Selection, pointer hover and keyboard focus have separate treatments. Native combo boxes, checkboxes, table headers and scrollbars share the same theme; dropdown menus retain keyboard navigation. Navigation uses one built-in Windows icon family. No runtime font or web dependency is added.

Spacing follows a 4 px rhythm with 2 px optical adjustments inside controls. Typography uses Windows interface fonts; format identifiers, frame rates and file sizes use tabular figures. Panels remain bounded, with scrolling confined to lists and logs. The compact sidebar is deliberately narrower than a browser dashboard to preserve the existing 1040 × 640 window requirement.

The source thumbnail carries the content color. Format counts and aligned figures add detail without decorative charts or invented data. The existing generated GrabForge logo remains the product mark. Features, section order, preset schema and portable data locations are unchanged. Feedback uses surface and focus changes without decorative animation.

Design review used [unslop](https://github.com/heydamir/unslop) by Damir Sokolovsky ([@heydamir](https://x.com/heydamir)), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Its web-oriented guidance was adapted to the existing native WPF interface.
