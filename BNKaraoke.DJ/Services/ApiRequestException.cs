using System;
using System.Collections.Generic;
using System.Net;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.Services
{
    public class ApiRequestException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public IReadOnlyList<ReorderWarning> Warnings { get; }
        public string? CurrentVersion { get; }

        public ApiRequestException(
            string message,
            HttpStatusCode statusCode,
            IReadOnlyList<ReorderWarning>? warnings = null,
            string? currentVersion = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            Warnings = warnings ?? Array.Empty<ReorderWarning>();
            CurrentVersion = currentVersion;
        }
    }
}
