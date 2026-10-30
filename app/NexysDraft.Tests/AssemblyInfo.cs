using Xunit;

// The gate classes share samples/gen, work/dxf and the LibreDWG/ODA processes: run them one class at a time (no races on the
// converted DXF files, and dxf2dwg timings are not distorted by a parallel dxf2dwg).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
