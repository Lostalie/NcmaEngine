using Ncma.Samples;
using System.Text.Json;
if(args.Length!=2){Console.Error.WriteLine("Usage: Ncma.SampleBuilder <output-parent> <built-gameplay-assembly>");return 2;}
try{Console.WriteLine(JsonSerializer.Serialize(ActionSample.Create(args[0],args[1]),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));return 0;}
catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
