all-source-files = \
	$(shell find . -name '*.cs' | grep -vE '(obj|tests)') \
	src/ecsc.sln

.PHONY: all
all: src/ecsc/bin/Release/ecsc.exe

src/ecsc/bin/Release/ecsc.exe: $(all-source-files)
	msbuild /p:Configuration=Release src/ecsc.sln
	touch $@
