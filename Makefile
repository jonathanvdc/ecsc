all-source-files = \
	$(shell find src -name '*.cs' -o -name '*.csproj' | grep -v 'obj') \
	src/ecsc.sln

.PHONY: all
all: src/ecsc/bin/Release/ecsc.exe

src/ecsc/bin/Release/ecsc.exe: $(all-source-files)
	msbuild /p:Configuration=Release src/ecsc.sln
	touch $@

.PHONY: nuget
nuget:
	nuget restore src/ecsc.sln
