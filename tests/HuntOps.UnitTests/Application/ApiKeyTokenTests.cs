using System.Text;
using HuntOps.Application.Access;

namespace HuntOps.UnitTests.Application;

public sealed class ApiKeyTokenTests
{
    [Fact]
    public void Generated_key_has_expected_shape_and_prefix()
    {
        var key = ApiKeyToken.Generate();

        Assert.Equal(ApiKeyToken.TokenLength, key.Plaintext.Length);
        Assert.StartsWith(key.Prefix + "_", key.Plaintext, StringComparison.Ordinal);
        Assert.Matches("^hops_[a-z0-9]{8}$", key.Prefix);
        Assert.True(ApiKeyToken.TryGetPrefix(key.Plaintext, out var prefix));
        Assert.Equal(key.Prefix, prefix);
    }

    [Fact]
    public void Stored_hash_is_sha256_and_does_not_contain_the_key()
    {
        var key = ApiKeyToken.Generate();

        Assert.Equal(32, key.Hash.Length);
        Assert.DoesNotContain(key.Plaintext, Encoding.UTF8.GetString(key.Hash), StringComparison.Ordinal);
        Assert.True(ApiKeyToken.Matches(key.Plaintext, key.Hash));
    }

    [Fact]
    public void A_modified_key_does_not_match()
    {
        var key = ApiKeyToken.Generate();
        var last = key.Plaintext[^1];
        var tampered = key.Plaintext[..^1] + (last == 'A' ? 'B' : 'A');

        Assert.False(ApiKeyToken.Matches(tampered, key.Hash));
    }

    [Fact]
    public void Keys_are_unique()
    {
        var keys = Enumerable.Range(0, 200).Select(_ => ApiKeyToken.Generate()).ToList();

        Assert.Equal(keys.Count, keys.Select(k => k.Plaintext).Distinct().Count());
        Assert.Equal(keys.Count, keys.Select(k => k.Prefix).Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer hops_abcdefgh_xxx")]
    [InlineData("hops_abcdefgh_tooShort")]
    public void Malformed_values_are_rejected(string? value)
    {
        Assert.False(ApiKeyToken.TryGetPrefix(value, out _));
    }

    [Fact]
    public void Wrong_scheme_uppercase_id_bad_separator_and_bad_characters_are_rejected()
    {
        var valid = ApiKeyToken.Generate().Plaintext;
        var id = valid.Substring(5, 8);
        var secret = valid[14..];

        Assert.False(ApiKeyToken.TryGetPrefix("hopx" + valid[4..], out _));
        Assert.False(ApiKeyToken.TryGetPrefix($"hops_ABCDEFGH_{secret}", out _));
        Assert.False(ApiKeyToken.TryGetPrefix($"hops_{id}-{secret}", out _));
        Assert.False(ApiKeyToken.TryGetPrefix($"hops_{id}_{secret[..^1]}!", out _));
    }
}
