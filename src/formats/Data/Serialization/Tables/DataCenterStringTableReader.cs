using Vezel.Novadrop.Data.Serialization.Items;
using Vezel.Novadrop.Data.Serialization.Regions;

namespace Vezel.Novadrop.Data.Serialization.Tables;

internal sealed class DataCenterStringTableReader
{
    private readonly DataCenterSegmentedRegion<DataCenterRawChar> _data = new();

    private readonly DataCenterSegmentedSimpleRegion<DataCenterRawString> _strings;

    private readonly DataCenterSimpleRegion<DataCenterRawAddress> _addresses = new(offByOne: true);

    private readonly Dictionary<DataCenterAddress, string> _byAddress = new(ushort.MaxValue);

    private readonly List<string> _byIndex = new(ushort.MaxValue);

    public DataCenterStringTableReader(int count)
    {
        _strings = new(count);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask ReadAsync(
        DataCenterArchitecture architecture,
        bool strict,
        StreamBinaryReader reader,
        CancellationToken cancellationToken)
    {
        await _data.ReadAsync(architecture, reader, cancellationToken).ConfigureAwait(false);
        await _strings.ReadAsync(architecture, reader, cancellationToken).ConfigureAwait(false);
        await _addresses.ReadAsync(architecture, reader, cancellationToken).ConfigureAwait(false);

        Check.Data(
            !strict || _data.Segments.Count <= DataCenterAddress.MaxValue.SegmentIndex,
            $"String table is too large ({_data.Segments.Count} segments).");

        var cache = new List<(int Index, string Value)>(ushort.MaxValue);

        foreach (var (i, seg) in _strings.Segments.Select(static (seg, i) => (i, seg)))
        {
            var last = -1L;

            foreach (var str in seg.Elements)
            {
                var index = str.Index - 1;

                Check.Data(
                    index >= 0 && index < _addresses.Elements.Count,
                    $"String index {index + 1} is out of bounds (1..{_addresses.Elements.Count}).");

                var length = str.Length - 1; // Includes the terminator.

                Check.Data(length >= 0, $"String has invalid length {length + 1}.");

                var addr = str.Address;
                var segIdx = addr.SegmentIndex;
                var segs = _data.Segments;

                Check.Data(segIdx < segs.Count, $"String segment index {segIdx} is out of bounds (0..{segs.Count}).");

                var elemIdx = addr.ElementIndex;
                var elems = segs[segIdx].Elements;

                // Note that if the string straddles the end of the segment, the terminator may be omitted.
                Check.Data(
                    elemIdx + length < elems.Count,
                    $"String range {elemIdx}..{elemIdx + length + 1} is out of bounds (0..{elems.Count - 1}).");

                var value = new string([.. elems.GetRange(elemIdx, length).Select(static c => c.Value)]);

                if (strict)
                {
                    var realAddr = _addresses.Elements[index];

                    Check.Data(
                        addr == realAddr, $"String address {addr} does not match expected address {realAddr}.");

                    var hash = str.Hash;
                    var realHash = DataCenterHash.ComputeStringHash(value);

                    Check.Data(
                        hash == realHash, $"String hash 0x{hash:x8} does not match expected hash 0x{realHash:x8}.");
                    Check.Data(hash >= last, $"String hash 0x{hash:x8} is less than previous hash (0x{last:x8}).");

                    last = hash;

                    var bucket = (hash ^ hash >> 16) % (uint)_strings.Segments.Count;

                    Check.Data(i == bucket, $"String bucket {i} does not match expected bucket {bucket}.");
                }

                Check.Data(_byAddress.TryAdd(addr, value), $"String address {addr} already recorded earlier.");

                cache.Add((index, value));
            }
        }

        _byIndex.AddRange(cache.OrderBy(static tup => tup.Index).Select(static tup => tup.Value));
    }

    public string GetString(int index)
    {
        Check.Data(index < _byIndex.Count, $"String table index {index} is out of bounds (0..{_byIndex.Count}).");

        return _byIndex[index];
    }

    public string GetString(DataCenterAddress address)
    {
        Check.Data(_byAddress.TryGetValue(address, out var str), $"String table address {address} is invalid.");

        return str;
    }
}
