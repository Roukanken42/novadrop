using Vezel.Novadrop.Schema;

namespace Vezel.Novadrop.Commands;

[SuppressMessage("", "CA1812")]
internal sealed class SchemaCommand : CancellableAsyncCommand<SchemaCommand.SchemaCommandSettings>
{
    public sealed class SchemaCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<input>")]
        [Description("Input file")]
        public string Input { get; }

        [CommandArgument(1, "<output>")]
        [Description("Output directory")]
        public string Output { get; }

        [CommandOption("--decryption-key <key>")]
        [Description("Set decryption key")]
        [TypeConverter(typeof(HexStringConverter))]
        public ReadOnlyMemory<byte> DecryptionKey { get; init; } = DataCenter.LatestKey;

        [CommandOption("--decryption-iv <iv>")]
        [Description("Set decryption IV")]
        [TypeConverter(typeof(HexStringConverter))]
        public ReadOnlyMemory<byte> DecryptionIV { get; init; } = DataCenter.LatestIV;

        [CommandOption("--strict")]
        [Description("Enable strict verification")]
        public bool Strict { get; init; }

        [CommandOption("--strategy <strategy>")]
        [Description("Set inference strategy")]
        public DataCenterSchemaStrategy Strategy { get; init; }

        [CommandOption("--subdirectories")]
        [Description("Enable output subdirectories based on data sheet names")]
        public bool Subdirectories { get; init; }

        public SchemaCommandSettings(string input, string output)
        {
            Input = input;
            Output = output;
        }
    }

    protected override Task PreExecuteAsync(
        dynamic expando, SchemaCommandSettings settings, CancellationToken cancellationToken)
    {
        expando.Handler = new DataSheetValidationHandler();

        return Task.CompletedTask;
    }

    protected override async Task<int> ExecuteAsync(
        dynamic expando, SchemaCommandSettings settings, ProgressContext progress, CancellationToken cancellationToken)
    {
        Log.WriteLine(
            """
            [yellow]Schemas generated by this command are not completely accurate.

            While schemas generated from a given data center file will correctly validate the
            exact data tree one might unpack from that file, certain nuances of the data can
            only be inferred on a best-effort basis. For example, it is impossible to accurately
            infer whether a given path with repeated node names in the tree is truly recursive,
            or whether an attribute that is always present is truly required.

            This means that schemas generated from this command may reject modifications to the
            data tree that are actually correct, e.g. as interpreted by the TERA client.

            Users who intend to modify a data center using schemas generated from this command
            should generate schemas with both the conservative and aggressive strategies, compare
            the resulting schemas, and construct a more accurate set of schemas using good human
            judgement.[/]
            """.ReplaceLineEndings());
        Log.WriteLine();
        Log.MarkupLineInterpolated(
            $"Inferring data sheet schemas of [cyan]{settings.Input}[/] to [cyan]{settings.Output}[/] with strategy [cyan]{settings.Strategy}[/]...");

        var files = await progress.RunTaskAsync(
            "Gather data sheet files",
            () => Task.FromResult(
                new DirectoryInfo(settings.Input)
                    .EnumerateFiles("?*.xml", SearchOption.AllDirectories)
                    .OrderBy(f => f.FullName, StringComparer.Ordinal)
                    .Select((f, i) => (Index: i, File: f))
                    .ToArray()));

        var handler = (DataSheetValidationHandler)expando.Handler;

        var nodes = await progress.RunTaskAsync(
            "Load data sheets",
            files.Length,
            increment => Task.WhenAll(
                files
                    .AsParallel()
                    .WithCancellation(cancellationToken)
                    .Select(item => Task.Run(
                        async () =>
                        {
                            var file = item.File;
                            var xmlSettings = new XmlReaderSettings { Async = true };

                            using var reader = XmlReader.Create(file.FullName, xmlSettings);

                            XDocument doc;

                            try
                            {
                                doc = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
                            }
                            catch (XmlException ex)
                            {
                                handler.HandleException(file, ex);

                                return default;
                            }

                            increment();
                            return doc.Root;
                        },
                        cancellationToken))));
        var nonNullNodes = from node in nodes where node is not null select node;
        var inferable = new XmlInferableList(nonNullNodes.ToList());

        // var root = await progress.RunTaskAsync(
        //     "Load data center",
        //     async () =>
        //     {
        //         await using var inStream = File.OpenRead(settings.Input);
        //
        //         return await DataCenter.LoadAsync(
        //             inStream,
        //             new DataCenterLoadOptions()
        //                 .WithKey(settings.DecryptionKey.Span)
        //                 .WithIV(settings.DecryptionIV.Span)
        //                 .WithStrict(settings.Strict)
        //                 .WithLoaderMode(DataCenterLoaderMode.Eager)
        //                 .WithMutability(DataCenterMutability.Immutable),
        //             cancellationToken);
        //     });
        //
        // var inferable = new DataCenterInferableNode(root);

        var schema = await progress.RunTaskAsync(
            "Infer data sheet schemas",
            inferable.Children.Count,
            increment =>
                Task.FromResult(
                    DataCenterSchemaInference.Infer(settings.Strategy, inferable, increment, cancellationToken)));

        var output = new DirectoryInfo(settings.Output);

        await progress.RunTaskAsync(
            "Write data sheet schemas",
            schema.Children.Count + 1,
            async increment =>
            {
                output.Create();

                var sheetOutput = settings.Subdirectories ? output : output.CreateSubdirectory("sheets");
                var xmlSettings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    IndentChars = "    ",
                    NewLineHandling = NewLineHandling.Entitize,
                    Async = true,
                };

                await Parallel.ForEachAsync(
                    schema.Children,
                    cancellationToken,
                    async (item, cancellationToken) =>
                    {
                        await WriteSchemaAsync(
                            settings.Subdirectories ? sheetOutput.CreateSubdirectory(item.Key) : sheetOutput,
                            item.Key,
                            xmlSettings,
                            item.Value.Node,
                            cancellationToken);

                        increment();
                    });

#pragma warning disable CS0436 // TODO: https://github.com/dotnet/Nerdbank.GitVersioning/issues/555
                await using var inXsd = typeof(ThisAssembly).Assembly.GetManifestResourceStream("DataCenter.xsd");
#pragma warning restore CS0436

                if (inXsd != null)
                {
                    await using var outXsd = File.Open(
                        Path.Combine(output.FullName, "DataCenter.xsd"), FileMode.Create, FileAccess.Write);

                    await inXsd.CopyToAsync(outXsd, cancellationToken);
                }

                increment();
            });

        return 0;
    }

    private static async Task WriteSchemaAsync(
        DirectoryInfo directory,
        string name,
        XmlWriterSettings xmlSettings,
        DataCenterNodeSchema nodeSchema,
        CancellationToken cancellationToken)
    {
        var writtenTypes = new Dictionary<DataCenterNodeSchema, string>();

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
        async ValueTask WriteAttributesAsync(XmlWriter xmlWriter, DataCenterNodeSchema nodeSchema)
        {
            if (nodeSchema.Attributes.Count == 0)
                return;

            foreach (var (name, attrSchema) in nodeSchema.Attributes.OrderBy(n => n.Key, StringComparer.Ordinal))
            {
                await xmlWriter.WriteStartElementAsync("xsd", "attribute", null);

                {
                    await xmlWriter.WriteAttributeStringAsync(null, "name", null, name);
                    await xmlWriter.WriteAttributeStringAsync(null, "type", null, attrSchema.TypeCode switch
                    {
                        DataCenterTypeCode.Int32 => "xsd:int",
                        DataCenterTypeCode.Single => "xsd:float",
                        DataCenterTypeCode.String => "xsd:string",
                        DataCenterTypeCode.Boolean => "xsd:boolean",
                        _ => throw new UnreachableException(),
                    });

                    if (!attrSchema.IsOptional)
                        await xmlWriter.WriteAttributeStringAsync(null, "use", null, "required");
                }

                await xmlWriter.WriteEndElementAsync();
            }
        }

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
        async ValueTask WriteComplexTypeAsync(XmlWriter xmlWriter, string typeName, DataCenterNodeSchema nodeSchema)
        {
            cancellationToken.ThrowIfCancellationRequested();

            writtenTypes.Add(nodeSchema, typeName);

            await xmlWriter.WriteStartElementAsync("xsd", "complexType", null);

            {
                await xmlWriter.WriteAttributeStringAsync(null, "name", null, typeName);

                if (nodeSchema.HasKeys)
                    await xmlWriter.WriteAttributeStringAsync(
                        "dc", "keys", null, string.Join(" ", nodeSchema.Keys.Distinct()));

                if (nodeSchema.HasMixedContent)
                    await xmlWriter.WriteAttributeStringAsync(null, "mixed", null, "true");

                if (nodeSchema.Children.Count != 0)
                {
                    await xmlWriter.WriteStartElementAsync("xsd", "sequence", null);

                    foreach (var (childName, edge) in nodeSchema.Children.OrderBy(n => n.Key, StringComparer.Ordinal))
                    {
                        var fullChildName = writtenTypes.GetValueOrDefault(edge.Node) ?? $"{typeName}_{childName}";

                        await xmlWriter.WriteStartElementAsync("xsd", "element", null);
                        await xmlWriter.WriteAttributeStringAsync(null, "name", null, childName);
                        await xmlWriter.WriteAttributeStringAsync(null, "type", null, fullChildName);

                        if (edge.IsOptional)
                            await xmlWriter.WriteAttributeStringAsync(null, "minOccurs", null, "0");

                        if (edge.IsRepeatable)
                            await xmlWriter.WriteAttributeStringAsync(null, "maxOccurs", null, "unbounded");

                        await xmlWriter.WriteEndElementAsync();
                    }

                    await xmlWriter.WriteEndElementAsync();
                }

                if (nodeSchema.HasValue && !nodeSchema.HasMixedContent)
                {
                    await xmlWriter.WriteStartElementAsync("xsd", "simpleContent", null);

                    {
                        await xmlWriter.WriteStartElementAsync("xsd", "extension", null);

                        {
                            await xmlWriter.WriteAttributeStringAsync(null, "base", null, "xsd:string");

                            await WriteAttributesAsync(xmlWriter, nodeSchema);
                        }

                        await xmlWriter.WriteEndElementAsync();
                    }

                    await xmlWriter.WriteEndElementAsync();
                }
                else
                    await WriteAttributesAsync(xmlWriter, nodeSchema);
            }

            await xmlWriter.WriteEndElementAsync();

            if (nodeSchema.Children.Count != 0)
                foreach (var (childName, child) in nodeSchema.Children.OrderBy(n => n.Key, StringComparer.Ordinal))
                    if (!writtenTypes.ContainsKey(child.Node))
                        await WriteComplexTypeAsync(xmlWriter, $"{typeName}_{childName}", child.Node);
        }

        await using var textWriter = new StreamWriter(Path.Combine(directory.FullName, $"{name}.xsd"));

        await using (var xmlWriter = XmlWriter.Create(textWriter, xmlSettings))
        {
            const string baseUri = "https://vezel.dev/novadrop/dc";

            await xmlWriter.WriteStartElementAsync("xsd", "schema", "http://www.w3.org/2001/XMLSchema");

            {
                await xmlWriter.WriteAttributeStringAsync(
                    "xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
                await xmlWriter.WriteAttributeStringAsync(
                    "xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                await xmlWriter.WriteAttributeStringAsync("xmlns", "dc", null, baseUri);
                await xmlWriter.WriteAttributeStringAsync(null, "xmlns", null, $"{baseUri}/{name}");
                await xmlWriter.WriteAttributeStringAsync(null, "targetNamespace", null, $"{baseUri}/{name}");
                await xmlWriter.WriteAttributeStringAsync(
                    "xsi", "schemaLocation", null, $"{baseUri} ../DataCenter.xsd");
                await xmlWriter.WriteAttributeStringAsync(null, "elementFormDefault", null, "qualified");

                await WriteComplexTypeAsync(xmlWriter, name, nodeSchema);

                await xmlWriter.WriteStartElementAsync("xsd", "element", null);

                {
                    await xmlWriter.WriteAttributeStringAsync(null, "name", null, name);
                    await xmlWriter.WriteAttributeStringAsync(null, "type", null, name);
                }

                await xmlWriter.WriteEndElementAsync();
            }

            await xmlWriter.WriteEndElementAsync();
        }

        await textWriter.WriteLineAsync();
    }

    protected override Task PostExecuteAsync(
        dynamic expando, SchemaCommandSettings settings, CancellationToken cancellationToken)
    {
        expando.Handler.Print();

        return Task.CompletedTask;
    }
}
