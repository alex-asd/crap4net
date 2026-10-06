using Crap4Net;

return new CliApplication(Directory.GetCurrentDirectory(), Console.Out, Console.Error, new ProcessCommandRunner())
    .Execute(args);
