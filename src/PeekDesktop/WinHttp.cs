using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PeekDesktop;

/// <summary>
/// Minimal WinHTTP wrapper for making HTTPS GET requests.
/// Replaces System.Net.Http.HttpClient to avoid pulling in the entire
/// managed networking stack (~1 MB in the AOT binary).
/// </summary>
internal static class WinHttp
{
    /// <summary>
    /// Downloads a file from a URL and saves it to disk. Used for fetching release zip assets.
    /// Runs synchronously — call from a background thread to keep UI responsive.
    /// </summary>
    public static void DownloadToFile(string url, string userAgent, string destinationPath, int timeoutSeconds = 60)
    {
        IntPtr hSession = IntPtr.Zero;
        IntPtr hConnect = IntPtr.Zero;
        IntPtr hRequest = IntPtr.Zero;

        try
        {
            var components = new URL_COMPONENTS
            {
                dwStructSize = (uint)Marshal.SizeOf<URL_COMPONENTS>(),
                dwHostNameLength = unchecked((uint)-1),
                dwUrlPathLength = unchecked((uint)-1),
                dwExtraInfoLength = unchecked((uint)-1)
            };

            if (!WinHttpCrackUrl(url, (uint)url.Length, 0, ref components))
                throw new InvalidOperationException($"WinHttpCrackUrl failed: {Marshal.GetLastWin32Error()}");

            string hostName = Marshal.PtrToStringUni(components.lpszHostName, (int)components.dwHostNameLength)!;
            string urlPath = Marshal.PtrToStringUni(components.lpszUrlPath, (int)components.dwUrlPathLength)!;
            if (components.dwExtraInfoLength > 0)
                urlPath += Marshal.PtrToStringUni(components.lpszExtraInfo, (int)components.dwExtraInfoLength);

            hSession = WinHttpOpen(userAgent, WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, null, null, 0);
            if (hSession == IntPtr.Zero)
                throw new InvalidOperationException($"WinHttpOpen failed: {Marshal.GetLastWin32Error()}");

            int timeoutMs = timeoutSeconds * 1000;
            WinHttpSetTimeouts(hSession, timeoutMs, timeoutMs, timeoutMs, timeoutMs);

            hConnect = WinHttpConnect(hSession, hostName, components.nPort, 0);
            if (hConnect == IntPtr.Zero)
                throw new InvalidOperationException($"WinHttpConnect failed: {Marshal.GetLastWin32Error()}");

            uint flags = (components.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
            hRequest = WinHttpOpenRequest(hConnect, "GET", urlPath, null, null, null, flags);
            if (hRequest == IntPtr.Zero)
                throw new InvalidOperationException($"WinHttpOpenRequest failed: {Marshal.GetLastWin32Error()}");

            WinHttpAddRequestHeaders(hRequest, "Accept: application/octet-stream\r\n",
                unchecked((uint)-1), WINHTTP_ADDREQ_FLAG_ADD);

            if (!WinHttpSendRequest(hRequest, IntPtr.Zero, 0, IntPtr.Zero, 0, 0, 0))
                throw new InvalidOperationException($"WinHttpSendRequest failed: {Marshal.GetLastWin32Error()}");

            if (!WinHttpReceiveResponse(hRequest, IntPtr.Zero))
                throw new InvalidOperationException($"WinHttpReceiveResponse failed: {Marshal.GetLastWin32Error()}");

            uint statusCode = QueryStatusCode(hRequest);
            if (statusCode < 200 || statusCode >= 300)
                throw new InvalidOperationException($"HTTP {statusCode}");

            const int maxBytes = 50 * 1024 * 1024; // 50 MB
            byte[] buffer = new byte[65536];
            int totalRead = 0;

            using var fs = new System.IO.FileStream(destinationPath, System.IO.FileMode.Create,
                System.IO.FileAccess.Write, System.IO.FileShare.None);

            while (true)
            {
                if (!WinHttpQueryDataAvailable(hRequest, out uint bytesAvailable))
                    break;
                if (bytesAvailable == 0)
                    break;

                uint toRead = Math.Min(bytesAvailable, (uint)buffer.Length);
                if (!WinHttpReadData(hRequest, buffer, toRead, out uint bytesRead))
                    break;
                if (bytesRead == 0)
                    break;

                totalRead += (int)bytesRead;
                if (totalRead > maxBytes)
                    throw new InvalidOperationException($"Download exceeded {maxBytes / (1024 * 1024)} MB limit");

                fs.Write(buffer, 0, (int)bytesRead);
            }
        }
        finally
        {
            if (hRequest != IntPtr.Zero) WinHttpCloseHandle(hRequest);
            if (hConnect != IntPtr.Zero) WinHttpCloseHandle(hConnect);
            if (hSession != IntPtr.Zero) WinHttpCloseHandle(hSession);
        }
    }

    /// <summary>
    /// Performs a synchronous HTTPS GET and returns the response body as a UTF-8 string.
    /// Throws on any failure (connection, HTTP error status, etc.).
    /// </summary>
    public static string Get(string url, string userAgent, int timeoutSeconds = 10)
    {
        IntPtr hSession = IntPtr.Zero;
        IntPtr hConnect = IntPtr.Zero;
        IntPtr hRequest = IntPtr.Zero;

        try
        {
            // Parse the URL
            var components = new URL_COMPONENTS
            {
                dwStructSize = (uint)Marshal.SizeOf<URL_COMPONENTS>(),
                dwHostNameLength = unchecked((uint)-1),
                dwUrlPathLength = unchecked((uint)-1),
                dwExtraInfoLength = unchecked((uint)-1)
            };

            if (!WinHttpCrackUrl(url, (uint)url.Length, 0, ref components))
                throw new InvalidOperationException($"WinHttpCrackUrl failed: {Marshal.GetLastWin32Error()}");

            string hostName = Marshal.PtrToStringUni(components.lpszHostName, (int)components.dwHostNameLength)!;
            string urlPath = Marshal.PtrToStringUni(components.lpszUrlPath, (int)components.dwUrlPathLength)!;
            if (components.dwExtraInfoLength > 0)
                urlPath += Marshal.PtrToStringUni(components.lpszExtraInfo, (int)components.dwExtraInfoLength);

            // Open session
            hSession = WinHttpOpen(userAgent, WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, null, null, 0);
            if (hSession == IntPtr.Zero)
                throw new InvalidOperationException($"WinHttpOpen failed: {Marshal.GetLastWin32Error()}");

            // Set timeouts (resolve, connect, send, receive) in milliseconds
            int timeoutMs = timeoutSeconds * 1000;
            WinHttpSetTimeouts(hSession, timeoutMs, timeoutMs, timeoutMs, timeoutMs);

            // Connect
            hConnect = WinHttpConnect(hSession, hostName, components.nPort, 0);
            if (hConnect == IntPtr.Zero)
                throw new InvalidOperationException($"WinHttpConnect failed: {Marshal.GetLastWin32Error()}");

            // Open request
            uint flags = (components.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
            hRequest = WinHttpOpenRequest(hConnect, "GET", urlPath, null, null, null, flags);
            if (hRequest == IntPtr.Zero)
                throw new InvalidOperationException($"WinHttpOpenRequest failed: {Marshal.GetLastWin32Error()}");

            // Add Accept header for GitHub API
            WinHttpAddRequestHeaders(hRequest, "Accept: application/vnd.github+json\r\n",
                unchecked((uint)-1), WINHTTP_ADDREQ_FLAG_ADD);

            // Send
            if (!WinHttpSendRequest(hRequest, IntPtr.Zero, 0, IntPtr.Zero, 0, 0, 0))
                throw new InvalidOperationException($"WinHttpSendRequest failed: {Marshal.GetLastWin32Error()}");

            // Receive response
            if (!WinHttpReceiveResponse(hRequest, IntPtr.Zero))
                throw new InvalidOperationException($"WinHttpReceiveResponse failed: {Marshal.GetLastWin32Error()}");

            // Check status code
            uint statusCode = QueryStatusCode(hRequest);
            if (statusCode < 200 || statusCode >= 300)
                throw new InvalidOperationException($"HTTP {statusCode}");

            // Read body (cap at 1 MB to prevent memory exhaustion)
            const int maxResponseBytes = 1024 * 1024;
            var sb = new StringBuilder(4096);
            byte[] buffer = new byte[8192];
            int totalRead = 0;

            while (true)
            {
                if (!WinHttpQueryDataAvailable(hRequest, out uint bytesAvailable))
                    break;
                if (bytesAvailable == 0)
                    break;

                uint toRead = Math.Min(bytesAvailable, (uint)buffer.Length);
                if (!WinHttpReadData(hRequest, buffer, toRead, out uint bytesRead))
                    break;
                if (bytesRead == 0)
                    break;

                totalRead += (int)bytesRead;
                if (totalRead > maxResponseBytes)
                    throw new InvalidOperationException($"Response exceeded {maxResponseBytes} byte limit");

                sb.Append(Encoding.UTF8.GetString(buffer, 0, (int)bytesRead));
            }

            return sb.ToString();
        }
        finally
        {
            if (hRequest != IntPtr.Zero) WinHttpCloseHandle(hRequest);
            if (hConnect != IntPtr.Zero) WinHttpCloseHandle(hConnect);
            if (hSession != IntPtr.Zero) WinHttpCloseHandle(hSession);
        }
    }

    private static uint QueryStatusCode(IntPtr hRequest)
    {
        uint statusCode = 0;
        uint size = sizeof(uint);
        WinHttpQueryHeaders(hRequest,
            WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
            IntPtr.Zero, ref statusCode, ref size, IntPtr.Zero);
        return statusCode;
    }

    // --- Constants ---
    private const uint WINHTTP_ACCESS_TYPE_DEFAULT_PROXY = 0;
    private const uint WINHTTP_FLAG_SECURE = 0x00800000;
    private const ushort INTERNET_SCHEME_HTTPS = 2;
    private const uint WINHTTP_QUERY_STATUS_CODE = 19;
    private const uint WINHTTP_QUERY_FLAG_NUMBER = 0x20000000;
    private const uint WINHTTP_ADDREQ_FLAG_ADD = 0x20000000;

    // --- Structs ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct URL_COMPONENTS
    {
        public uint dwStructSize;
        public IntPtr lpszScheme;
        public uint dwSchemeLength;
        public ushort nScheme;
        public IntPtr lpszHostName;
        public uint dwHostNameLength;
        public ushort nPort;
        public IntPtr lpszUserName;
        public uint dwUserNameLength;
        public IntPtr lpszPassword;
        public uint dwPasswordLength;
        public IntPtr lpszUrlPath;
        public uint dwUrlPathLength;
        public IntPtr lpszExtraInfo;
        public uint dwExtraInfoLength;
    }

    // --- P/Invoke ---
    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpCrackUrl(string pwszUrl, uint dwUrlLength, uint dwFlags, ref URL_COMPONENTS lpUrlComponents);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr WinHttpOpen(string? pszAgentW, uint dwAccessType, string? pszProxyW, string? pszProxyBypassW, uint dwFlags);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpSetTimeouts(IntPtr hInternet, int nResolveTimeout, int nConnectTimeout, int nSendTimeout, int nReceiveTimeout);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr WinHttpConnect(IntPtr hSession, string pswzServerName, ushort nServerPort, uint dwReserved);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr WinHttpOpenRequest(IntPtr hConnect, string pwszVerb, string pwszObjectName, string? pwszVersion, string? pwszReferrer, string? ppwszAcceptTypes, uint dwFlags);

    [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpAddRequestHeaders(IntPtr hRequest, string lpszHeaders, uint dwHeadersLength, uint dwModifiers);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpSendRequest(IntPtr hRequest, IntPtr lpszHeaders, uint dwHeadersLength, IntPtr lpOptional, uint dwOptionalLength, uint dwTotalLength, uint dwContext);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpReceiveResponse(IntPtr hRequest, IntPtr lpReserved);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpQueryHeaders(IntPtr hRequest, uint dwInfoLevel, IntPtr pwszName, ref uint lpBuffer, ref uint lpdwBufferLength, IntPtr lpdwIndex);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpQueryDataAvailable(IntPtr hRequest, out uint lpdwNumberOfBytesAvailable);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpReadData(IntPtr hRequest, byte[] lpBuffer, uint dwNumberOfBytesToRead, out uint lpdwNumberOfBytesRead);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpCloseHandle(IntPtr hInternet);
}
