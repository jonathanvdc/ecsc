# Build Status

Linux | Windows
----- | -------
[![Build Status](https://travis-ci.org/jonathanvdc/ecsc.svg?branch=master)](https://travis-ci.org/jonathanvdc/ecsc) | [![Build status](https://ci.appveyor.com/api/projects/status/6t6whsqeiebiggbc?svg=true)](https://ci.appveyor.com/project/jonathanvdc/ecsc)

# ecsc

`ecsc` is an [Enhanced C#](http://ecsharp.net/) compiler that uses Flame as its back-end. It can be used to compile both C# and EC# source code files, and ships with the following features:
* EC#'s [lexical macro processor (LeMP)](http://ecsharp.net/lemp/)
* EC# language extensions, such as the `using` cast.
* Flame's aggressive ahead-of-time compiler optimizations

Caveat: `ecsc` is still a work-in-progress. It does not support a number of C# language features that everyone takes for granted nowadays, like nullable value types, lambdas and type argument inference. Furthermore, it can at times get the C# semantics wrong. Feel free to open an issue (or a pull request!) if you run into an issue like this.

## Build instructions

First, clone the `ecsc` repository, and navigate to the `ecsc` folder.

```
git clone https://github.com/jonathanvdc/ecsc
cd ecsc
```

Next, update the NuGet packages, and compile the `ecsc` solution.
Visual Studio users should be able to open `src/ecsc.sln`, switch to 'Release' mode, and hit 'Build Solution'.

For mono users, that's

```
nuget restore src/ecsc.sln
xbuild /p:Configuration=Release src/ecsc.sln
```

You should be all set now. Maybe you also want to add `ecsc` to your `PATH` environment variable, but I'll leave that up to you.

## Using `ecsc`

`ecsc` should be pretty straightforward to use. Once you've installed `ecsc` and added it to your `PATH` environment variable, you can compile files like so:

```
ecsc code.cs ecs-code.ecs -platform clr
```

Note the `-platform clr` option, which instructs `ecsc` to generate code for the CLR. This is almost always what you want. Also, source files must be specified _first_, before any other option. If you want to specify source files later on, you must use the `-source` option. 

```
ecsc -platform clr -source code.cs ecs-code.ecs 
```

By default, `ecsc` will put your output file(s) in a folder called `bin`. If you want them to end up elsewhere, use the `-o` option.

```
ecsc code.cs ecs-code.ecs -platform clr -o a.exe
```
