/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.InteropServices;
using System.Windows;

namespace Launcher.App.Services;

internal enum AuthlibInjectorServerDropStatus
{
    NotRecognized,
    Invalid,
    Valid
}

internal readonly record struct AuthlibInjectorServerDropResult(
    AuthlibInjectorServerDropStatus Status,
    string? AuthenticationServer = null);

internal static class AuthlibInjectorServerDropParser
{
    internal const string Prefix = "authlib-injector:yggdrasil-server:";
    internal const int MaximumTextLength = 4096;

    private static readonly string[] TextFormats =
    [
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.StringFormat
    ];

    public static AuthlibInjectorServerDropResult Parse(IDataObject dataObject)
    {
        foreach (var format in TextFormats)
        {
            try
            {
                if (!dataObject.GetDataPresent(format, autoConvert: true)
                    || dataObject.GetData(format, autoConvert: true) is not string text)
                {
                    continue;
                }

                return Parse(text);
            }
            catch (COMException)
            {
                // 跨进程拖放源可能在读取期间退出；继续尝试其余标准文本格式。
            }
            catch (InvalidOperationException)
            {
                // 数据对象可能只声明格式但无法在当前时刻完成自动转换。
            }
        }

        return new(AuthlibInjectorServerDropStatus.NotRecognized);
    }

    public static AuthlibInjectorServerDropResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new(AuthlibInjectorServerDropStatus.NotRecognized);

        var trimmed = text.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return new(AuthlibInjectorServerDropStatus.NotRecognized);

        // 以 trimmed 度量：上限限制的是待解码的载荷，前后空白不该把合法链接顶出范围。
        if (trimmed.Length > MaximumTextLength)
            return new(AuthlibInjectorServerDropStatus.Invalid);

        var encodedAddress = trimmed[Prefix.Length..];
        if (encodedAddress.Length == 0 || !HasValidPercentEncoding(encodedAddress))
            return new(AuthlibInjectorServerDropStatus.Invalid);

        string decodedAddress;
        try
        {
            decodedAddress = Uri.UnescapeDataString(encodedAddress).Trim();
        }
        catch (UriFormatException)
        {
            return new(AuthlibInjectorServerDropStatus.Invalid);
        }

        if (decodedAddress.Length == 0)
            return new(AuthlibInjectorServerDropStatus.Invalid);
        if (!decodedAddress.Contains("://", StringComparison.Ordinal))
            decodedAddress = $"https://{decodedAddress}";

        if (!Uri.TryCreate(decodedAddress, UriKind.Absolute, out var serverUri)
            || !string.Equals(serverUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(serverUri.Host))
        {
            return new(AuthlibInjectorServerDropStatus.Invalid);
        }

        return new(AuthlibInjectorServerDropStatus.Valid, serverUri.AbsoluteUri);
    }

    private static bool HasValidPercentEncoding(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
                continue;
            if (index + 2 >= value.Length
                || !Uri.IsHexDigit(value[index + 1])
                || !Uri.IsHexDigit(value[index + 2]))
            {
                return false;
            }

            index += 2;
        }

        return true;
    }
}
