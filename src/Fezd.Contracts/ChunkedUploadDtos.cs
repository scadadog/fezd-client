using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Request body for <c>POST /api/v1/projects/uploads</c>.</summary>
    public sealed class ChunkedUploadInitRequestDto
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; }

        /// <summary>Lower-case hex SHA-256 of the whole project file.</summary>
        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }

        /// <summary>Total byte size of the project file.</summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>Client-requested max part size in KiB (optional; server clamps).</summary>
        [JsonPropertyName("chunkSizeKb")]
        public int? ChunkSizeKb { get; set; }
    }

    /// <summary>Response from initiating a chunked upload session.</summary>
    public sealed class ChunkedUploadInitResultDto
    {
        [JsonPropertyName("uploadId")]
        public string UploadId { get; set; }

        [JsonPropertyName("chunkSizeBytes")]
        public long ChunkSizeBytes { get; set; }

        [JsonPropertyName("totalParts")]
        public int TotalParts { get; set; }

        /// <summary>UTC ISO-8601 when the staging session expires if not completed.</summary>
        [JsonPropertyName("expiresAt")]
        public string ExpiresAt { get; set; }
    }

    /// <summary>ACK after a successful part PUT.</summary>
    public sealed class ChunkedUploadPartAckDto
    {
        [JsonPropertyName("uploadId")]
        public string UploadId { get; set; }

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("receivedParts")]
        public int ReceivedParts { get; set; }

        [JsonPropertyName("totalParts")]
        public int TotalParts { get; set; }
    }

    /// <summary>Status of an in-progress (or closed) chunked upload session.</summary>
    public sealed class ChunkedUploadStatusDto
    {
        [JsonPropertyName("uploadId")]
        public string UploadId { get; set; }

        /// <summary><c>open</c>, <c>complete</c>, or <c>aborted</c>.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("receivedParts")]
        public int ReceivedParts { get; set; }

        [JsonPropertyName("totalParts")]
        public int TotalParts { get; set; }

        [JsonPropertyName("missingIndexes")]
        public List<int> MissingIndexes { get; set; }

        [JsonPropertyName("expiresAt")]
        public string ExpiresAt { get; set; }
    }
}
