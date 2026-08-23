using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed class GitHubAppJwtSigner(
    GitHubPrivateKeyFileReader _keyFileReader,
    IOptions<GitHubAppOptions> _options,
    TimeProvider _timeProvider)
{
  private static readonly byte[] _header =
      """{"alg":"RS256","typ":"JWT"}"""u8.ToArray();

  public async Task<GitHubClientOutcome<string>> CreateAsync(
      CancellationToken cancellationToken)
  {
    var keyOutcome = await _keyFileReader.ReadAsync(cancellationToken);
    if (keyOutcome.Kind != GitHubClientOutcomeKind.Success ||
        keyOutcome.Value is null)
    {
      return new(
          keyOutcome.Kind,
          null,
          keyOutcome.RetryAt,
          keyOutcome.Detail);
    }

    var keyBytes = keyOutcome.Value;
    try
    {
      using var rsa = RSA.Create();
      var keyCharacters = GC.AllocateUninitializedArray<char>(
          Encoding.UTF8.GetCharCount(keyBytes));
      try
      {
        var characterCount = Encoding.UTF8.GetChars(
            keyBytes,
            keyCharacters);
        rsa.ImportFromPem(keyCharacters.AsSpan(0, characterCount));
      }
      catch (ArgumentException)
      {
        return Failure("private-key-malformed");
      }
      catch (CryptographicException)
      {
        return Failure("private-key-malformed");
      }
      finally
      {
        Array.Clear(keyCharacters);
      }

      var now = _timeProvider.GetUtcNow();
      var issuedAt = now.AddSeconds(-30).ToUnixTimeSeconds();
      var expiresAt = now.AddMinutes(9).ToUnixTimeSeconds();
      var payload = JsonSerializer.SerializeToUtf8Bytes(
          new GitHubAppJwtPayload(_options.Value.AppId, issuedAt, expiresAt),
          GitHubJsonContext.Default.GitHubAppJwtPayload);
      try
      {
        var header = Base64Url.EncodeToString(_header);
        var body = Base64Url.EncodeToString(payload);
        var signingInput = Encoding.ASCII.GetBytes($"{header}.{body}");
        try
        {
          var signature = rsa.SignData(
              signingInput,
              HashAlgorithmName.SHA256,
              RSASignaturePadding.Pkcs1);
          try
          {
            return new(
                GitHubClientOutcomeKind.Success,
                $"{header}.{body}.{Base64Url.EncodeToString(signature)}",
                null,
                null);
          }
          finally
          {
            CryptographicOperations.ZeroMemory(signature);
          }
        }
        finally
        {
          CryptographicOperations.ZeroMemory(signingInput);
        }
      }
      finally
      {
        CryptographicOperations.ZeroMemory(payload);
      }
    }
    finally
    {
      CryptographicOperations.ZeroMemory(keyBytes);
    }
  }

  private static GitHubClientOutcome<string> Failure(string detail) =>
      new(GitHubClientOutcomeKind.InvalidRequest, null, null, detail);
}
