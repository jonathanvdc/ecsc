# Build Status

Linux | Windows
----- | -------
[![Build Status](https://travis-ci.org/jonathanvdc/ecsc.svg?branch=master)](https://travis-ci.org/jonathanvdc/ecsc) | [![Build status](https://ci.appveyor.com/api/projects/status/6t6whsqeiebiggbc?svg=true)](https://ci.appveyor.com/project/jonathanvdc/ecsc)

# ecsc
A Flame-based EC# compiler. (WIP)

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
