# Help Centre integration

The desktop project now contains a data-driven `HelpCentreView`. Its home screen presents one tile for every approved application area. Keyboard Shortcuts is fully populated; the remaining tiles provide a useful overview and explicitly identify that detailed step-by-step guidance will follow.

## Main-window integration hooks

The shell owner should place one `HelpCentreView` in the shared workspace host and keep the instance alive so search and topic navigation remain responsive.

- Sidebar Help: show the Help Centre workspace and call `OpenTopic(HelpCentreRegistry.HomeTopicId)`.
- `F1`: execute `HelpCommands.OpenHelpCentre`, then call `ShowContextHelp(currentDestination, currentFeatureCode)`.
- `Ctrl + /`: execute `HelpCommands.OpenKeyboardShortcuts`, then call `OpenTopic(HelpCentreRegistry.KeyboardShortcutsTopicId)`.
- `NavigationRequested`: close Help and navigate to the supplied destination and optional feature code.
- `CloseRequested`: return through the normal navigation-history service so the previous workspace and filters are restored.

The command bindings belong at the main-window level. Bindings must check whether the requested action is enabled and must not override text editing behaviour. `F1` and `Ctrl + /` do not conflict with ordinary text input.

## Extending help later

Add a `HelpTopicDefinition` to `HelpCentreRegistry.Topics`. Each topic requires a title, one-line description, overview and searchable keywords. A workspace may then route context help through `ContextHelpRouter`. Change availability to `Available` when its detailed guide is complete; no tile should ever open an empty page.

The shortcut guide is generated from `HelpCentreRegistry.Shortcuts`, keeping visible help text and executable shortcut contracts in one place.
