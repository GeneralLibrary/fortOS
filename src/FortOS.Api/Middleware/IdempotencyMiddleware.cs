using System.Security.Cryptography;
using System.Text;
using FortOS.Core;
using FortOS.Security.Models;

namespace FortOS.Api.Middleware;

/// <summary>Persistent Idempotency-Key: same subject and request fingerprint can be replayed, pending requests return explicit conflict.</summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private const int DefaultMaximumBody = 1024 * 1024;

    public async Task InvokeAsync(HttpContext context, IDatabaseProvider database)
    {
        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPut(context.Request.Method) && !HttpMethods.IsPatch(context.Request.Method) && !HttpMethods.IsDelete(context.Request.Method))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        if (key.Length > 200)
        {
            await ApiProblem.WriteAsync(context, StatusCodes.Status400BadRequest, "IDEMPOTENCY_KEY_INVALID", "Idempotency-Key is too long.").ConfigureAwait(false);
            return;
        }

        var maxBody = configuration.GetValue("idempotency:max_body_bytes", DefaultMaximumBody);
        if (context.Request.ContentLength is > 0 and var length && length > maxBody)
        {
            await ApiProblem.WriteAsync(context, StatusCodes.Status413PayloadTooLarge, "IDEMPOTENCY_BODY_TOO_LARGE", "The request body cannot be cached safely.").ConfigureAwait(false);
            return;
        }
        context.Request.EnableBuffering(maxBody);
        var hash = await FingerprintAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        context.Request.Body.Position = 0;
        var subject = (context.Items["NasTokenPayload"] as NasTokenPayload)?.Sub ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        var ttl = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("idempotency:ttl_minutes", 60), 1, 1440));

        await database.InitializeAsync(context.RequestAborted).ConfigureAwait(false);
        var acquired = await AcquireAsync(database, key, subject, context.Request.Method, context.Request.Path, hash, ttl, context.RequestAborted).ConfigureAwait(false);
        if (acquired.Replay is not null)
        {
            context.Response.StatusCode = acquired.Replay.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(acquired.Replay.Response, context.RequestAborted).ConfigureAwait(false);
            return;
        }
        if (!acquired.Acquired)
        {
            await ApiProblem.WriteAsync(context, StatusCodes.Status409Conflict, acquired.Code, "An identical request is already in progress or the key was reused.").ConfigureAwait(false);
            return;
        }

        var original = context.Response.Body;
        await using var captured = new MemoryStream();
        context.Response.Body = captured;
        try
        {
            await next(context).ConfigureAwait(false);
            if (captured.Length <= maxBody)
            {
                captured.Position = 0;
                var body = await new StreamReader(captured, Encoding.UTF8, leaveOpen: true).ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
                await CompleteAsync(database, key, subject, context.Request.Method, context.Request.Path, context.Response.StatusCode, body, context.RequestAborted).ConfigureAwait(false);
            }
            else await DeleteAsync(database, key, context.RequestAborted).ConfigureAwait(false);
            captured.Position = 0;
            await captured.CopyToAsync(original, context.RequestAborted).ConfigureAwait(false);
        }
        catch
        {
            await DeleteAsync(database, key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally { context.Response.Body = original; }
    }

    private static async Task<string> FingerprintAsync(HttpRequest request, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Include the query string: two requests with the same key, method and body but different
        // query parameters are distinct operations and must not be treated as a replay.
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}{request.QueryString}\n"));
        var buffer = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<(bool Acquired, string Code, IdempotencyReplay? Replay)> AcquireAsync(IDatabaseProvider database, string key, string subject, string method, PathString path, string hash, TimeSpan ttl, CancellationToken ct)
    {
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        // BEGIN IMMEDIATE：并发携带相同 Idempotency-Key 的请求必须在写前串行化，
        // 否则两个请求同时读到「无记录」并各自 INSERT，触发主键冲突（500）。
        await using var transaction = connection.BeginTransaction(deferred: false);
        var now = DateTimeOffset.UtcNow;
        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "DELETE FROM idempotency_records WHERE expires_at <= $now; SELECT subject, method, path, request_hash, state, status_code, response_json FROM idempotency_records WHERE idempotency_key = $key;";
        lookup.Parameters.AddWithValue("$now", now.ToString("O"));
        lookup.Parameters.AddWithValue("$key", key);
        await using var reader = await lookup.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var same = reader.GetString(0) == subject && reader.GetString(1) == method && reader.GetString(2) == path && reader.GetString(3) == hash;
            var pending = reader.GetString(4) == "pending";
            var replay = same && !pending ? new IdempotencyReplay(reader.GetInt32(5), reader.GetString(6)) : null;
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return (false, same && pending ? "IDEMPOTENCY_PENDING" : "IDEMPOTENCY_KEY_REUSED", replay);
        }
        await reader.DisposeAsync().ConfigureAwait(false);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT OR IGNORE INTO idempotency_records(idempotency_key,subject,method,path,status_code,response_json,expires_at,request_hash,state,updated_at) VALUES($key,$subject,$method,$path,0,'',$expires,$hash,'pending',$now);";
        insert.Parameters.AddWithValue("$key", key); insert.Parameters.AddWithValue("$subject", subject); insert.Parameters.AddWithValue("$method", method); insert.Parameters.AddWithValue("$path", path.ToString());
        insert.Parameters.AddWithValue("$expires", now.Add(ttl).ToString("O")); insert.Parameters.AddWithValue("$hash", hash); insert.Parameters.AddWithValue("$now", now.ToString("O"));
        // INSERT OR IGNORE 在 key 已存在（并发窗口）时静默跳过并返回 0 行：
        // 视为冲突由调用方返回 409，而不是让主键约束异常冒泡成 500。
        var inserted = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return inserted == 0
            ? (false, "IDEMPOTENCY_KEY_REUSED", null)
            : (true, string.Empty, null);
    }

    private static async Task CompleteAsync(IDatabaseProvider database, string key, string subject, string method, PathString path, int status, string response, CancellationToken ct)
    {
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE idempotency_records SET status_code=$status,response_json=$body,state='completed',updated_at=$now WHERE idempotency_key=$key AND subject=$subject AND method=$method AND path=$path;";
        command.Parameters.AddWithValue("$status", status); command.Parameters.AddWithValue("$body", response); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$subject", subject); command.Parameters.AddWithValue("$method", method); command.Parameters.AddWithValue("$path", path.ToString());
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task DeleteAsync(IDatabaseProvider database, string key, CancellationToken ct)
    {
        await using var connection = await database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM idempotency_records WHERE idempotency_key=$key;";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private sealed record IdempotencyReplay(int StatusCode, string Response);
}
