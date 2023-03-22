namespace Vezel.Novadrop.Commands;

[SuppressMessage("", "CA1812")]
internal sealed class UnpackCommand : CancellableAsyncCommand<UnpackCommand.UnpackCommandSettings>
{
    public sealed class UnpackCommandSettings : CommandSettings
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
        public ReadOnlyMemory<byte> DecryptionKey { get; init; } = DataCenter.Build100Key;

        [CommandOption("--decryption-iv <iv>")]
        [Description("Set decryption IV")]
        [TypeConverter(typeof(HexStringConverter))]
        public ReadOnlyMemory<byte> DecryptionIV { get; init; } = DataCenter.Build100IV;

        [CommandOption("--strict")]
        [Description("Enable strict verification")]
        public bool Strict { get; init; }

        public UnpackCommandSettings(string input, string output)
        {
            Input = input;
            Output = output;
        }
    }

    protected override Task PreExecuteAsync(
        dynamic expando, UnpackCommandSettings settings, CancellationToken cancellationToken)
    {
        expando.Missing = new List<string>();

        return Task.CompletedTask;
    }

    protected override async Task<int> ExecuteAsync(
        dynamic expando, UnpackCommandSettings settings, ProgressContext progress, CancellationToken cancellationToken)
    {
        Log.MarkupLineInterpolated($"Unpacking [cyan]{settings.Input}[/] to [cyan]{settings.Output}[/]...");

        var root = await progress.RunTaskAsync(
            "Load data center",
            async () =>
            {
                await using var inStream = File.OpenRead(settings.Input);

                return await DataCenter.LoadAsync(
                    inStream,
                    new DataCenterLoadOptions()
                        .WithKey(settings.DecryptionKey.Span)
                        .WithIV(settings.DecryptionIV.Span)
                        .WithStrict(settings.Strict),
                    cancellationToken);
            });

        var output = new DirectoryInfo(settings.Output);
        var sheets = root.Children;
        var sheetNames = sheets.Select(n => n.Name).Distinct().ToArray();
        var missing = (List<string>)expando.Missing;

        await progress.RunTaskAsync(
            "Write data sheet schemas",
            sheetNames.Length + 1,
            async increment =>
            {
                output.Create();

                [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
                [SuppressMessage("", "CS0436")]
                async ValueTask WriteSchemaAsync(DirectoryInfo directory, string name)
                {
                    var xsdName = $"{name}.xsd";

                    // TODO: https://github.com/dotnet/Nerdbank.GitVersioning/issues/555
#pragma warning disable CS0436
                    await using var inXsd = typeof(ThisAssembly).Assembly.GetManifestResourceStream(xsdName);
#pragma warning restore CS0436

                    // Is this not a data sheet we recognize?
                    if (inXsd == null)
                    {
                        missing.Add(name);

                        return;
                    }

                    await using var outXsd = File.Open(
                        Path.Combine(directory.FullName, xsdName), FileMode.Create, FileAccess.Write);

                    await inXsd.CopyToAsync(outXsd, cancellationToken);

                    increment();
                }

                await Parallel.ForEachAsync(
                    sheetNames,
                    cancellationToken,
                    async (name, cancellationToken) =>
                        await WriteSchemaAsync(output.CreateSubdirectory(name), name));

                await WriteSchemaAsync(output, "DataCenter");
            });

        await progress.RunTaskAsync(
            "Write data sheets",
            sheets.Count,
            increment =>
            {
                var xmlSettings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    IndentChars = "    ",
                    NewLineHandling = NewLineHandling.Entitize,
                    Async = true,
                };

                return Parallel.ForEachAsync(
                    sheets
                        .GroupBy(n => n.Name, (name, elems) => NameGroupOfOutputFiles(elems))
                        .SelectMany(elems => elems),
                    cancellationToken,
                    async (item, cancellationToken) =>
                    {
                        var node = item.Node;

                        await using var textWriter = new StreamWriter(Path.Combine(
                            output.CreateSubdirectory(node.Name).FullName, item.FileName));

                        await using (var xmlWriter = XmlWriter.Create(textWriter, xmlSettings))
                        {
                            [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
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

                        increment();
                    });
            });

        return 0;
    }

    private static readonly Dictionary<string, List<string>> _namings = new()
    {
        { "Accessory", new() { "id" } },
        { "Area", new() { "continentId", "areaName" } },
        { "ClimbingTerritory", new() { "continentId", "areaName" } },
        { "Dungeon", new() { "continentId" } },
        { "TerritoryData", new() { "huntingZoneId" } },
        { "ShieldTerritory", new() { "continentId", "areaName" } },
        { "SkillData", new() { "huntingZoneId", "templateId" } },
        { "Quest", new() { "id" } },
        { "QuestDialog", new() { "huntingZoneId", "id" } },
        { "NpcData", new() { "huntingZoneId" } },
        { "NpcSoundData", new() { "huntingZoneId" } },
        { "VillagerDialog", new() { "huntingZoneId", "id" } },
        { "MovieScript", new() { "id" } },
    };

    private static string NameOutputFile(DataCenterNode node)
    {
        var specifier = _namings.TryGetValue(node.Name, out var keys)
            ? string.Join(
                '-',
                keys
                    .Where(s => node.Attributes.ContainsKey(s) && node.Attributes[s].ToString() != "0")
                    .Select(key => $"{key}-{node.Attributes[key]}"))
            : string.Empty;
        return string.IsNullOrEmpty(specifier) ? $"{node.Name}" : $"{node.Name}-{specifier}";
    }

    private static IEnumerable<(DataCenterNode Node, string FileName)> NameGroupOfOutputFiles(IEnumerable<DataCenterNode> nodes)
    {
        return nodes
            .GroupBy(NameOutputFile)
            .SelectMany(group =>
                group.Count() == 1
                    ? group.Select(node => (node, $"{group.Key}.xml"))
                    : group.Select((node, index) => (node, $"{group.Key}-{index:d5}.xml")));
    }

    protected override Task PostExecuteAsync(
        dynamic expando, UnpackCommandSettings settings, CancellationToken cancellationToken)
    {
        foreach (var name in (List<string>)expando.Missing)
            Log.MarkupLineInterpolated($"[yellow]Data sheet [cyan]{name}[/] does not have a known schema.[/]");

        return Task.CompletedTask;
    }
}
