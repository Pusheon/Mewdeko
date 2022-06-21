﻿using Discord.Commands;
using System.Threading.Tasks;

namespace Mewdeko.Common.TypeReaders;

public class TypeReaderCollection : TypeReader
{
    private readonly IEnumerable<TypeReader> _readers;

    public TypeReaderCollection(IEnumerable<TypeReader> readers) => _readers = readers;

    public override async Task<TypeReaderResult> ReadAsync(
        ICommandContext context, string input, IServiceProvider services)
    {
        var success = new List<TypeReaderValue>();
        var errors = new List<TypeReaderResult>();

        foreach (var reader in _readers)
        {
            var result = await reader.ReadAsync(context, input, services).ConfigureAwait(false);
            if (result.Error is not null)
                errors.Add(result);
            else
                success.AddRange(result.Values);
        }

        return success.Count == 0 && errors.Count > 0
            ? errors.First()
            : TypeReaderResult.FromSuccess(success);
    }
}