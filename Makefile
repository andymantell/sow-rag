.PHONY: test e2e-test e2e-test-headed install-playwright

test:
	dotnet test SoWImprover.Tests/SoWImprover.Tests.csproj --verbosity normal

e2e-test: install-playwright
	dotnet test SoWImprover.E2E/SoWImprover.E2E.csproj --verbosity normal

e2e-test-headed: install-playwright
	PLAYWRIGHT_HEADED=1 dotnet test SoWImprover.E2E/SoWImprover.E2E.csproj --verbosity normal

install-playwright:
	dotnet build SoWImprover.E2E/SoWImprover.E2E.csproj
	node SoWImprover.E2E/bin/Debug/net8.0/.playwright/package/cli.js install --with-deps chromium
