using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Achilles.Domain.Billing;
using Achilles.Domain.Webhooks;
using Achilles.Protocol;

namespace Achilles.Cli.Commands;

public static class BillingCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleBillingAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintBillingHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "TEST" or "SIMULATE" => await HandleTestWebhookAsync(args[1..]).ConfigureAwait(false),
            "VERIFY-SIG" => HandleVerifySig(args[1..]),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleTestWebhookAsync(string[] args)
    {
        string provider = (GetArg(args, "--provider") ?? "stripe").ToLowerInvariant();
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string email = GetArg(args, "--email") ?? "customer@example.com";
        string customerId = GetArg(args, "--customer-id") ?? $"cus_{Guid.NewGuid():N}"[..14];
        string? eventType = GetArg(args, "--event");
        string? seatsStr = GetArg(args, "--seats");
        int seats = int.TryParse(seatsStr, out int s) ? s : 3;
        bool jsonOutput = HasFlag(args, "--json");

        string secret;
        string endpointPath;
        string signatureHeaderName;
        string signatureHeaderValue;
        string payloadJson;

        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        switch (provider)
        {
            case "stripe":
                eventType ??= "checkout.session.completed";
                secret = GetArg(args, "--secret") ?? "whsec_stripe_test_secret_1234567890";
                endpointPath = "/v1/billing/stripe/webhook";
                signatureHeaderName = "Stripe-Signature";

                payloadJson = JsonSerializer.Serialize(new
                {
                    id = $"evt_{Guid.NewGuid():N}",
                    type = eventType,
                    data = new
                    {
                        @object = new
                        {
                            id = $"cs_{Guid.NewGuid():N}",
                            customer = customerId,
                            customer_details = new
                            {
                                email = email,
                                name = "Acme Corp"
                            },
                            subscription = $"sub_{Guid.NewGuid():N}"[..18],
                            metadata = new
                            {
                                seats = seats.ToString(CultureInfo.InvariantCulture)
                            }
                        }
                    }
                });

                signatureHeaderValue = WebhookSecurity.BuildSignatureHeader(secret, nowSec, payloadJson);
                break;

            case "lemonsqueezy" or "lemon":
                eventType ??= "order_created";
                secret = GetArg(args, "--secret") ?? "whsec_lemonsqueezy_test_secret_1234567890";
                endpointPath = "/v1/billing/lemonsqueezy/webhook";
                signatureHeaderName = "X-Signature";

                payloadJson = JsonSerializer.Serialize(new
                {
                    meta = new
                    {
                        event_name = eventType,
                        custom_data = new
                        {
                            seats = seats.ToString(CultureInfo.InvariantCulture)
                        }
                    },
                    data = new
                    {
                        id = $"ls_sub_{Guid.NewGuid():N}"[..16],
                        attributes = new
                        {
                            customer_id = 98765,
                            user_email = email,
                            user_name = "Acme Corp",
                            status = "active",
                            renews_at = DateTimeOffset.UtcNow.AddYears(1)
                        }
                    }
                });

                byte[] lemonKey = Encoding.UTF8.GetBytes(secret);
                byte[] lemonHash = HMACSHA256.HashData(lemonKey, Encoding.UTF8.GetBytes(payloadJson));
                signatureHeaderValue = Convert.ToHexStringLower(lemonHash);
                break;

            case "paddle":
                eventType ??= "transaction.completed";
                secret = GetArg(args, "--secret") ?? "whsec_paddle_test_secret_1234567890";
                endpointPath = "/v1/billing/paddle/webhook";
                signatureHeaderName = "Paddle-Signature";

                payloadJson = JsonSerializer.Serialize(new
                {
                    event_id = $"evt_{Guid.NewGuid():N}",
                    event_type = eventType,
                    data = new
                    {
                        id = $"sub_{Guid.NewGuid():N}"[..16],
                        customer_id = customerId,
                        status = "active",
                        items = new[]
                        {
                            new { quantity = seats, price = new { id = "pri_enterprise_annual" } }
                        },
                        custom_data = new
                        {
                            email = email,
                            seats = seats.ToString(CultureInfo.InvariantCulture)
                        }
                    }
                });

                string paddleStringToSign = $"{nowSec}:{payloadJson}";
                byte[] paddleKey = Encoding.UTF8.GetBytes(secret);
                byte[] paddleHash = HMACSHA256.HashData(paddleKey, Encoding.UTF8.GetBytes(paddleStringToSign));
                signatureHeaderValue = $"ts={nowSec};h1={Convert.ToHexStringLower(paddleHash)}";
                break;

            default:
                AnsiConsole.MarkupLine($"[red]Neznámy poskytovateľ:[/] {provider}. Podporovaní: stripe, lemonsqueezy, paddle.");
                return 1;
        }

        using var client = new HttpClient
        {
            BaseAddress = new Uri(serverEndpoint.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(15)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointPath);
        using var stringContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        request.Content = stringContent;
        request.Headers.Add(signatureHeaderName, signatureHeaderValue);

        try
        {
            var response = await client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[red]Server vrátil chybu:[/] {response.StatusCode} - {Markup.Escape(err)}");
                return 1;
            }

            var result = await response.Content.ReadFromJsonAsync(AchillesProtocolJsonContext.Default.BillingWebhookResponseDto).ConfigureAwait(false);
            if (result is null)
            {
                AnsiConsole.MarkupLine("[red]Prázdna odpoveď od servera.[/]");
                return 1;
            }

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Pole[/]");
            table.AddColumn("[bold]Hodnota[/]");

            table.AddRow("Provider", $"[bold cyan]{provider.ToUpperInvariant()}[/]");
            table.AddRow("Event", $"[yellow]{eventType}[/]");
            table.AddRow("Success", result.Success ? "[green]Áno[/]" : "[red]Nie[/]");
            table.AddRow("Action", $"[bold]{result.Action}[/]");
            if (!string.IsNullOrWhiteSpace(result.LicenseId)) table.AddRow("License ID", result.LicenseId);
            if (!string.IsNullOrWhiteSpace(result.LicenseKey)) table.AddRow("License Key (Crockford)", $"[bold green]{result.LicenseKey}[/]");
            if (!string.IsNullOrWhiteSpace(result.CustomerId)) table.AddRow("Customer ID", result.CustomerId);
            if (!string.IsNullOrWhiteSpace(result.SubscriptionId)) table.AddRow("Subscription ID", result.SubscriptionId);
            table.AddRow("Message", Markup.Escape(result.Message));

            var panel = new Panel(table)
            {
                Header = new PanelHeader($"[bold green] Inbound Billing Gateway Webhook ({provider}) [/]"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(panel);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba komunikácie so serverom:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static int HandleVerifySig(string[] args)
    {
        string provider = (GetArg(args, "--provider") ?? "stripe").ToLowerInvariant();
        string? secret = GetArg(args, "--secret");
        string? header = GetArg(args, "--header");
        string? payload = GetArg(args, "--payload");

        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(payload))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadať --secret <sec> --header <hdr> --payload <json>[/]");
            return 1;
        }

        IBillingWebhookProcessor processor = provider switch
        {
            "stripe" => new StripeBillingProcessor(),
            "lemonsqueezy" or "lemon" => new LemonSqueezyBillingProcessor(),
            "paddle" => new PaddleBillingProcessor(),
            _ => throw new ArgumentException($"Neznámy poskytovateľ: {provider}")
        };

        bool valid = processor.VerifySignature(secret, header, payload);
        if (valid)
        {
            AnsiConsole.MarkupLine($"[bold green] Kryptografický podpis webhooku pre '{provider}' je PLATNÝ.[/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red] Kryptografický podpis webhooku pre '{provider}' je NEPLATNÝ![/]");
            return 1;
        }
    }

    private static void PrintBillingHelp()
    {
        Console.WriteLine("""
            Použitie: symbolon billing <subcommand> [options]

            Subcommands:
              test [--provider <stripe|lemonsqueezy|paddle>] [--event <type>] [--seats <n>] [--email <addr>] [--server <url>] [--secret <whsec>] [--json]
                  Odoslať kryptograficky podpísaný testovací webhook na server a vykonať automatickú províziu alebo lifecycle synchronizáciu.
              verify-sig --provider <stripe|lemonsqueezy|paddle> --secret <sec> --header <hdr> --payload <json>
                  Nezávisle offline overiť HMAC-SHA256 podpis webhooku daného poskytovateľa.
            """);
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre billing:[/] {sub}");
        PrintBillingHelp();
        return 1;
    }
}
