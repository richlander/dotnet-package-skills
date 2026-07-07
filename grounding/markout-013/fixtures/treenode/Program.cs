// A dependency scan produced this hierarchy. Render it as a Markdown document (see task).
// The tree (each node has a name, some have a badge, some have children):
//
//   myapp
//   ├─ Newtonsoft.Json        (badge: "pkg")
//   └─ Serilog                (badge: "pkg")
//      └─ Serilog.Sinks.Console
//
// These are plain notes — the data carries no output formatting.

// TODO: print the dependency hierarchy to the console as Markdown, using the Markout
// library's TreeNode shape so it renders as an indented tree (with the badges shown).
// Produce the output through the library's serializer; do not hand-write the tree.
