all-source-files = \
	$(shell find src -name '*.cs' -o -name '*.csproj' | grep -v 'obj') \
	src/ecsc.sln

.PHONY: all
all: src/ecsc/bin/Release/ecsc.exe

src/ecsc/bin/Release/ecsc.exe: $(all-source-files)
	msbuild /p:Configuration=Release /verbosity:quiet /nologo src/ecsc.sln
	touch $@

.PHONY: nuget
nuget:
	nuget restore src/ecsc.sln -Verbosity quiet

include flame-make-scripts/use-compare-test.mk

COMPARE_TEST_ARGS:=-j
ifneq ($(TEST_FILTER),)
COMPARE_TEST_ARGS:=$(COMPARE_TEST_ARGS) --filter $(TEST_FILTER)
endif

.PHONY: test
test: all | compare-test
	$(COMPARE_TEST) all-tests-mono.test $(COMPARE_TEST_ARGS)
