using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace ZasLauncherGUI.Utility;

public class Utils
{
    public static async Task<List<string>> GetXmlFromISK(string url)
    {
        List<string> result = new List<string>();
        try
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate
            };
            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(10);
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/xml")
            );

            request.Headers.TryAddWithoutValidation(
                "hwid",
                GetHash(GetMacAddresses())
            );
            request.Headers.TryAddWithoutValidation(
                "Connection",
                "Keep-Alive"
            );
            request.Content = new StringContent("");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync();
            var xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(xml);
            foreach (XmlNode item in xmlDocument.DocumentElement!
                         .SelectNodes("items/item")!)
            {
                var line = item
                    ?.SelectSingleNode("nazev")
                    ?.InnerText;
                
                if (!String.IsNullOrEmpty(line))
                {
                    if (!String.IsNullOrEmpty(item?.SelectSingleNode("cesta_exe")?.InnerText) || 
                        (bool)item?.SelectSingleNode("parametry")?.InnerText.Contains("#RDP_ID") ||
                        (bool)item?.SelectSingleNode("parametry")?.InnerText.Contains("#CREATE_ISK_PATCH;"))
                    {
                        line += "|" + item?.SelectSingleNode("cesta_exe")?.InnerText;
                        if (!String.IsNullOrEmpty(item?.SelectSingleNode("parametry")?.InnerText))
                        {
                            line += "|" + item?.SelectSingleNode("parametry")?.InnerText;
                        }
                    }
                }
                result.Add(line);
            }
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard(
                    "Chyba komunikace",
                    "Nepodařilo se načíst data ze serveru.\n\n" + ex.Message,
                    ButtonEnum.Ok,
                    Icon.Error);
            await box.ShowAsync();
        }

        return result;
    }

    public static string GetMacAddresses()
    {
        var result = new StringBuilder();
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var adapter in nics)
            {
                var address = adapter.GetPhysicalAddress();
                var bytes = address.GetAddressBytes();
                if (bytes.Length == 0)
                    continue;
                result.Append("<mac_address>");
                for (var i = 0; i < bytes.Length; i++)
                {
                    result.Append(bytes[i].ToString("X2"));
                    if (i != bytes.Length - 1)
                        result.Append("-");
                }

                result.Append("</mac_address>");
            }
        }
        catch
        {
            // případně zalogovat
        }

        return result.ToString();
    }

    private static string GetHash(string s)
    {
        MD5 sec = new MD5CryptoServiceProvider();
        ASCIIEncoding enc = new ASCIIEncoding();
        byte[] bt = enc.GetBytes(s);
        return GetHexString(sec.ComputeHash(bt));
    }

    private static string GetHexString(byte[] bt)
    {
        string s = string.Empty;
        for (int i = 0; i < bt.Length; i++)
        {
            byte b = bt[i];
            int n, n1, n2;
            n = (int)b;
            n1 = n & 15;
            n2 = (n >> 4) & 15;
            if (n2 > 9)
                s += ((char)(n2 - 10 + (int)'A')).ToString();
            else
                s += n2.ToString();
            if (n1 > 9)
                s += ((char)(n1 - 10 + (int)'A')).ToString();
            else
                s += n1.ToString();
            if ((i + 1) != bt.Length && (i + 1) % 2 == 0) s += "-";
        }

        return s;
    }
}