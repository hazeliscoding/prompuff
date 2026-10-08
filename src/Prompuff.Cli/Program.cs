using System.Text;
using Prompuff.Cli;

// Prompts are full of curly quotes, accents and emoji; Windows consoles default to an older code page.
Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

try
{
    return await CliApp.RunAsync(args, Console.In, Console.Out, Console.Error, cancellationToken: cancel.Token);
}
catch (OperationCanceledException)
{
    return CliApp.Failed;
}
