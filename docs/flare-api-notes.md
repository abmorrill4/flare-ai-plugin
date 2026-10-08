# Flare Plug-In API — what we know (from Flare 2026 docs)

Source: https://help.madcapsoftware.com/flare2026/Content/Flare/Plug-In-API/Plugin-API.htm
Treat `docs/api-dump.txt` (reflection over the real DLL) as authoritative where they differ.

## Lifecycle
- Class library, **.NET Framework 4.0** per docs — but Flare 22's DLL targets **4.8**, so we build net48; references `B3.PluginAPIKit.dll` (ships inside
  `Flare.app`).
- Implement `IPlugin`: `IsActivated` (bool prop), `GetVersion()`, `GetAuthor()`,
  `GetDescription()`, `GetName()`, `Initialize(IHost)`, `Execute()`, `Stop()`.
- `Initialize` should only stash the `IHost`. Get contexts in `Execute()`.
- `Stop()`: detach events, revert UI, call `host.Dispose()`, set `IsActivated = false`.
- Deploy to `Flare.app\Plugins\<PluginName>\` (assemblies + resources). Enable via
  File > Options > Plugins; restart Flare.
- Relative paths passed to API methods resolve from the `Plugins` folder.

## IHost
- `GetCompany()`, `GetName()`, `GetEditorContext()`, `GetNavContext()`, `Dispose()`.

## IEditorContext
- `GetActiveDocument()` → `IDocument`; `GetDocuments()`; `OpenDocument(path[, EditorView])`
- `GetMadCapSchema()` → `XmlSchema` of MadCap element names
- event `DocumentSwitched`

## IDocument
Props: `CurrentEditorView`, `EnableLocks`, `Selection` (ISelection), `ShowChanges`, `ShowLocks`
Methods:
- `GetXmlDocument()` → `XmlDocument`; `GetDocumentXml()` → string; `GetDocumentText()`;
  `GetSourceUrl()`; `GetCursorPosition()`/`SetCursorPosition(int)`
- `InsertDocumentNode(XmlNode parent, XmlNode newNode, int index, bool trackChange)`
- `ReplaceDocumentNode(XmlNode old, XmlNode new, bool trackChange)`
- `RemoveDocumentNode(XmlNode node, bool trackChange)`
- `StartOperation(string name)` / `EndOperation()` — one undo entry
- `Select(string text)` / `Select(XmlNode)`; `Save()`; `UpdateView()`; `Close()`
- `ApplyStylesheet(path)` / `RemoveStylesheet(path)`; `LookupVariableValue(set, name)`
- `GetUserPendingContextMenuItems()` → `List<ToolStripItem>` (context-menu extension point)
Events (real names per dump): `ContentChanged`, `Saving/Saved/SavingAs/SavedAs`, `Closing`, `CurrentEditorViewChanged`,
mouse/key events, `Printing`, `CheckingIn`.

## ISelection
- `GetText()`, `GetXmlNodeList()` → `IList<XmlNode>`, `ReplaceText(string)`,
  `SetSelectionLength(int)`, `TrackChange` (get/set), formatting helpers
  (`Bold()`, `FormatStyle(css)`, `GetStyle()`, colors, fonts).

## INavContext / ribbon (WPF)
```csharp
IRibbon ribbon = navContext.GetRibbon();
IRibbonTab tab = ribbon.AddNewRibbonTab("PluginTab", "P");       // (label, keytip)
IRibbonGroup group = tab.AddNewRibbonGroup("PluginGroup");
Button b = group.AddRibbonButton(new RibbonControlData {
    Label = "Search and Highlight", Command = new ButtonCommand(), KeyTip = "S" });
IRibbonComboBox combo = group.AddRibbonCombobox(new RibbonMenuData { ... });
```
- `RibbonControlData.LargeImage` is a WPF `BitmapImage`; `Command` is an `ICommand`.
- Ribbon tab is only visible in Ribbon UI mode (not Tool Strip mode).
- Also available: `ICustomToolBar`, `IToolStripMenuItem` for toolbar/menu items.

## Flare topic XML shape (what the validator assumes)
```xml
<?xml version="1.0" encoding="utf-8"?>
<html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd" MadCap:lastBlockDepth="2" ...>
  <head>...</head>
  <body>
    <h1>Title</h1>
    <p>Text with <MadCap:variable name="General.CompanyName" /> and
       <MadCap:xref href="Other.htm">link</MadCap:xref>.</p>
    <MadCap:snippetBlock src="../Resources/Snippets/Note.flsnp" />
    <MadCap:dropDown><MadCap:dropDownHead>..</MadCap:dropDownHead><MadCap:dropDownBody>..</MadCap:dropDownBody></MadCap:dropDown>
  </body>
</html>
```
- XHTML elements are in **no namespace**; MadCap elements are in the MadCap namespace.
- Conditions: `MadCap:conditions="Default.Print"` attributes on any element.

## Docked panes (internal API, verified Flare 22)
- `INavContext.GetParentForm()` is a `B3.AppKit.Editor`; its `PaneDock` (`IPaneDock`) hosts every pane.
- A plugin pane = `new B3.AppKit.Pane { Name, Title, View = <subclass of B3.AppKit.PaneView (a Panel)> }`,
  then `dock.InstallPane(pane)` + `dock.PinPane(pane, DockStyle.Right)` (Flare still puts it in the left group).
  `UninstallPane` removes it. Pane names in `dock.Panes`: ContentExplorer, ProjectExplorer, HelpDynamic, StartPage, Document.
- `InstallPane(pane, parent, InstallPaneAction.Install)` *replaces* the parent's view (Dynamic Help vanished). Don't.
- Controls added to an already-visible pane never get `VisibleChanged`; start work explicitly.
- Not public API: keep every use in `UI/PanelHost.cs` (`FlarePane`), JIT-isolated, with the floating-window fallback.

## Verified write behavior (Flare 22, tested live 2026-10-08)
- `ReplaceDocumentNode(target, new, …)` keeps `target` (same object) and copies `new`'s content and
  attributes into it. Node references survive; `new` is never attached.
- `ReplaceDocumentNode` on a **text node** throws ("Cannot set the 'InnerXml'…"). Element targets only.
- Untracked replacement **strips** `MadCap:change` markup and `MadCap:changeData`: you cannot write tracked
  changes as markup yourself (the text of both sides stays inline — corrupts the topic).
- Tracked replacement of a small wrapper element gives word-level changes, but Flare's **Redo** of that
  operation throws inside `TextChange.Do`. Don't.
- `ISelection.ReplaceText` with `TrackChange = true` after `Select(...)` gives native word-level changes that
  undo **and redo** cleanly. It leaves `<SelectionMarker/>` (and Start/End) elements in the DOM — remove them
  untracked after each edit or they are saved into the topic and misplace the next replacement.
- `Select(string)` selects the first match; checking `Selection.GetXmlNodeList()` contains the intended text node
  only pins the node, not the offset. Widen the span until it is unique in its text node (`TextRuns.UniqueSpan`)
  and re-type the extra characters, or "you" will hit the "you" inside "your". Every edit splits text nodes: re-locate (text + context) before each edit, right-most first.
- One `StartOperation/EndOperation` around many edits = one Ctrl+Z. Flare assigns one Add/Remove change id
  pair per operation.
