# Help Centre integration

The desktop project contains a data-driven `HelpCentreView`. Its home screen presents one tile for every approved application area. Every live module topic contains substantive numbered guidance, and Keyboard Shortcuts is generated from the executable shortcut registry. Automated tests enforce owned-destination coverage in both directions and reject empty, overview-only or placeholder guides.

## Main-window integration hooks

The shell owner should place one `HelpCentreView` in the shared workspace host and keep the instance alive so search and topic navigation remain responsive.

- Sidebar Help: show the Help Centre workspace and call `OpenTopic(HelpCentreRegistry.HomeTopicId)`.
- `F1`: execute `HelpCommands.OpenHelpCentre`, then call `ShowContextHelp(currentDestination, currentFeatureCode)`.
- `Ctrl + /`: execute `HelpCommands.OpenKeyboardShortcuts`, then call `OpenTopic(HelpCentreRegistry.KeyboardShortcutsTopicId)`.
- `NavigationRequested`: close Help and navigate to the supplied destination and optional feature code.
- `CloseRequested`: return through the normal navigation-history service so the previous workspace and filters are restored.

The command bindings belong at the main-window level. Bindings must check whether the requested action is enabled and must not override text editing behaviour. `F1` and `Ctrl + /` do not conflict with ordinary text input.

## Extending help

Add a `HelpTopicDefinition` to `HelpCentreRegistry.Topics`. Each topic requires a title, one-line description, at least four accurate numbered steps and searchable keywords. A workspace may then route context help through `ContextHelpRouter`. Live topics must be `Available`, and a navigable topic must target a destination in `WorkspaceModuleOwnershipRegistry`; no tile may open an empty or placeholder page.

The shortcut guide is generated from `HelpCentreRegistry.Shortcuts`, keeping visible help text and executable shortcut contracts in one place.
