namespace Vezel.Novadrop.Commands;

sealed class UnpackCommand : Command
{
    public UnpackCommand()
        : base("unpack", "Unpack the contents of a data center file to a directory.")
    {
        var inputArg = new Argument<FileInfo>("input", "Input file");
        var outputArg = new Argument<DirectoryInfo>("output", "Output directory");
        var strictOpt = new Option<bool>("--strict", () => false, "Enable strict verification");

        Add(inputArg);
        Add(outputArg);
        Add(strictOpt);

        this.SetHandler(
            async (FileInfo input, DirectoryInfo output, bool strict, CancellationToken cancellationToken) =>
            {
                Console.WriteLine($"Unpacking '{input}' to '{output}'...");

                var sw = Stopwatch.StartNew();

                await using var stream = input.OpenRead();

                var dc = await DataCenter.LoadAsync(
                    stream,
                    new DataCenterLoadOptions()
                        .WithLoaderMode(DataCenterLoaderMode.Transient)
                        .WithStrict(strict)
                        .WithMutability(DataCenterMutability.Immutable),
                    cancellationToken);

                output.Create();

                async ValueTask WriteSchemaAsync(DirectoryInfo directory, string name)
                {
                    await using var inXsd = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

                    // TODO: Remove this when we finish all XSDs.
                    if (inXsd == null)
                        return;

                    await using var outXsd = File.Open(
                        Path.Combine(directory.FullName, name), FileMode.Create, FileAccess.Write);

                    await inXsd.CopyToAsync(outXsd, cancellationToken);
                }

                await WriteSchemaAsync(output, "DataCenter.xsd");

                var sheets = dc.Root.Children;

                await Parallel.ForEachAsync(
                    sheets.Select(n => n.Name).Distinct(),
                    cancellationToken,
                    async (name, cancellationToken) =>
                        await WriteSchemaAsync(output.CreateSubdirectory(name), $"{name}.xsd"));

                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    IndentChars = "    ",
                    NewLineHandling = NewLineHandling.Entitize,
                    Async = true,
                };

                await Parallel.ForEachAsync(
                    sheets
                        .GroupBy(n => n.Name, (name, elems) => elems.Select((n, i) => (Node: n, Index: i)))
                        .SelectMany(elems => elems),
                    cancellationToken,
                    async (item, cancellationToken) =>
                    {
                        var node = item.Node;

                        await using var textWriter = new StreamWriter(Path.Combine(
                            output.CreateSubdirectory(node.Name).FullName, $"{node.Name}-{item.Index}.xml"));

                        await using (var xmlWriter = XmlWriter.Create(textWriter, settings))
                        {
                            async ValueTask WriteSheetAsync(DataCenterNode current, bool top)
                            {
                                var uri = $"https://vezel.dev/novadrop/dc/{node.Name}";

                                await xmlWriter.WriteStartElementAsync(null, current.Name, top ? uri : null);

                                if (top)
                                {
                                    await xmlWriter.WriteAttributeStringAsync(
                                        "xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                                    await xmlWriter.WriteAttributeStringAsync(
                                        "xsi", "schemaLocation", null, $"{uri} {node.Name}.xsd");
                                }

                                if (current.HasAttributes)
                                    foreach (var (name, attr) in current.Attributes)
                                        await xmlWriter.WriteAttributeStringAsync(null, name, null, attr.ToString());

                                // Some ~225 nodes in official data center files have __value__ set even when they have
                                // children, but the strings are random symbols or broken XML tags. The fact that they
                                // are included is most likely a bug in the software used to pack those files. So, just
                                // drop the value in these cases.
                                if (current.Value != null && !current.HasChildren)
                                    await xmlWriter.WriteStringAsync(current.Value);

                                if (current.HasChildren)
                                    foreach (var child in current.Children.OrderBy(n => n.Name, StringComparer.Ordinal))
                                        await WriteSheetAsync(child, false);

                                await xmlWriter.WriteEndElementAsync();
                            }

                            await WriteSheetAsync(node, true);
                        }

                        await textWriter.WriteLineAsync();
                    });

                sw.Stop();

                Console.WriteLine($"Unpacked {sheets.Count} data sheets in {sw.Elapsed}.");
            },
            inputArg,
            outputArg,
            strictOpt);
    }
}