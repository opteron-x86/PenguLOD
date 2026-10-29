// A real child process exercises argument quoting, logging, failure and cancellation on both hosts.
string output = args[Array.IndexOf(args, "--out") + 1];
string behavior = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "behavior.txt")).Trim();
Console.WriteLine("FAKE_READY");
Console.Out.Flush();
if (behavior == "wait") Thread.Sleep(TimeSpan.FromMinutes(2));
if (behavior == "fail")
{
    Console.Error.WriteLine("fixture error");
    return 9;
}
Directory.CreateDirectory(output);
File.Copy(Path.Combine(AppContext.BaseDirectory, "seed.cgid"), Path.Combine(output, "Tamrielx0000y0000.cgid"), true);
File.WriteAllBytes(Path.Combine(output, "Tamrielx0001y0000.cgid"), new byte[4]);
if (behavior == "corrupt") File.WriteAllBytes(Path.Combine(output, "Tamrielx0000y0000.cgid"), [255, 255, 255, 255]);
Console.WriteLine("wrote 2 files");
return 0;
