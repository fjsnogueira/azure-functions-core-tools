using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Net.Sockets;
using System.Net;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class StartTests : BaseE2ETest
    {
        private const string _serverNotReady = "Host was not ready after 10 seconds";
        public StartTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task start_nodejs()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start"
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Http Functions:",
                    "HttpTrigger: [GET,POST] http://localhost:7071/api/HttpTrigger"
                },
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        result.Should().Be("Hello Test", because: "response from default function should be 'Hello {name}'");
                    }
                },
            }, _output);
        }

        [Fact]
        public async Task start_nodejs_with_inspect()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start --language-worker -- \"--inspect=5050\""
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Debugger listening on ws://127.0.0.1:5050"
                },
                Test = async (_, p) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    p.Kill();
                },
            }, _output);

        }

        [Fact]
        public async Task start_dotnet_csharp()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --build --port 7073"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                    }
                },
            }, _output);
        }

        [Fact]
        public async Task start_displays_error_on_invalid_function_json()
        {
            var functionName = "HttpTriggerJS";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node",
                        $"new --template \"Http Trigger\" --name {functionName}",
                    },
                    Test = async (workingDir, _) =>
                    {
                        var filePath = Path.Combine(workingDir, functionName, "function.json");
                        var functionJson = await File.ReadAllTextAsync(filePath);
                        functionJson = functionJson.Replace("\"type\": \"http\"", "\"type\": \"http2\"");
                        await File.WriteAllTextAsync(filePath, functionJson);
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = false,
                    OutputContains = new []
                    {
                        "The binding type(s) 'http2' are not registered. Please ensure the type is correct and the binding extension is installed."
                    },
                    Test = async (_, p) =>
                    {
                        // give the host time to load functions and print any errors
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        p.Kill();
                    }
                }
            }, _output);
        }

        [Fact]
        public async Task start_displays_error_on_invalid_host_json()
        {
            var functionName = "HttpTriggerCSharp";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        $"new --template Httptrigger --name {functionName}",

                    },
                    Test = async (workingDir, p) =>
                    {
                        var filePath = Path.Combine(workingDir, "host.json");
                        string hostJsonContent = "{ \"version\": \"2.0\", \"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\", \"version\": \"[1.*, 2.0.0)\" }}";
                        await File.WriteAllTextAsync(filePath, hostJsonContent);
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Extension bundle configuration should not be present" },
                },
            }, _output, startHost: true);
        }

       
        [Fact]
        public async Task start_displays_error_on_missing_host_json()
        {
            var functionName = "HttpTriggerCSharp";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        $"new --template Httptrigger --name {functionName}",
                    },
                    Test = async (workingDir, p) =>
                    {
                        var hostJsonPath = Path.Combine(workingDir, "host.json");
                        File.Delete(hostJsonPath);

                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Host.json file in missing" },
                },
            }, _output);
        }

        [Fact]
        public async Task start_host_port_in_use()
        {
            var tcpListner = new TcpListener(IPAddress.Any, 8081);
            try
            {
                tcpListner.Start();

                await CliTester.Run(new RunConfiguration
                {
                    Commands = new[]
                    {
                    "init . --worker-runtime node",
                    "new --template \"Http Trigger\" --name HttpTrigger",
                    "start --port 8081"
                },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Port 8081 is unavailable" }
                }, _output);
            }
            finally
            {
                tcpListner.Stop();
            }
        }

        [Fact]
        public async Task start_handles_empty_envvars_correctly()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node",
                        "new --template \"Http trigger\" --name HttpTrigger",
                        "settings add emptySetting EMPTY_VALUE",
                    },
                    Test = async (workingDir, p) =>
                    {
                        var settingsFile = Path.Combine(workingDir, "local.settings.json");
                        var content = File.ReadAllText(settingsFile);
                        content = content.Replace("EMPTY_VALUE", "");
                        File.WriteAllText(settingsFile,content);
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start --port 6543"
                    },
                    ExpectExit = false,
                    Test = async (w, p) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:6543/") })
                        {
                            client.Timeout = TimeSpan.FromSeconds(2);
                            for (var i = 0; i < 10; i++)
                            {
                                try
                                {
                                    var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                                    response.EnsureSuccessStatusCode();
                                    break;
                                }
                                catch
                                {
                                    await Task.Delay(TimeSpan.FromSeconds(2));
                                }
                            }
                        }
                        p.Kill();
                    },
                    OutputDoesntContain = new string[]
                    {
                        "Skipping 'emptySetting' from local settings as it's already defined in current environment variables."
                    }
                }
            }, _output);
        }

        [Fact]
        public async Task start_powershell()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime powershell",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        result.Should().Be("Hello Test", because: "response from default function should be 'Hello {name}'");
                    }
                },
            }, _output);
        }

        [Fact]
        public async Task only_run_some_functions()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime javascript",
                    "new --template \"Http trigger\" --name http1",
                    "new --template \"Http trigger\" --name http2",
                    "new --template \"Http trigger\" --name http3",
                    "start --functions http2 http1"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/http1?name=Test");
                        response.StatusCode.Should().Be(HttpStatusCode.OK);

                        response = await client.GetAsync("/api/http2?name=Test");
                        response.StatusCode.Should().Be(HttpStatusCode.OK);

                        response = await client.GetAsync("/api/http3?name=Test");
                        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
                        p.Kill();
                    }
                },
            }, _output);
        }

        private async Task<bool> WaitUntilReady(HttpClient client)
        {
            for (var limit = 0; limit < 10; limit++)
            {
                try
                {
                    var response = await client.GetAsync("/admin/host/ping");
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    await Task.Delay(1000);
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
            return false;
        }
    }
}