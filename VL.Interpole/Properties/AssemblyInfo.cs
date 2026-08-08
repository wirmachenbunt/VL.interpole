using VL.Core.Import;

// Imports this assembly's [ProcessNode] classes when referenced as a project.
// NOTE: the node-factory path (individual bang pin per item) does NOT run this way —
// that needs the library deployed as a package in the packs folder. The dynamic pin
// group is instead driven at edit time through the solution API (see InterpoleNode).
[assembly: ImportAsIs]
