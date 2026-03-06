.PHONY: test e2e-test e2e-test-headed install-playwright

test:
	dotnet test SoWImprover.Tests/SoWImprover.Tests.csproj --verbosity normal

e2e-test: install-playwright
	dotnet test SoWImprover.E2E/SoWImprover.E2E.csproj --verbosity normal

e2e-test-headed: install-playwright
	PLAYWRIGHT_HEADED=1 dotnet test SoWImprover.E2E/SoWImprover.E2E.csproj --verbosity normal

PLAYWRIGHT_CLI = SoWImprover.E2E/bin/Debug/net8.0/.playwright/package/cli.js

install-playwright:
	dotnet build SoWImprover.E2E/SoWImprover.E2E.csproj
	@node $(PLAYWRIGHT_CLI) install --dry-run chromium 2>/dev/null \
		| grep -q "Install location" \
		&& INSTALL_DIR=$$(node $(PLAYWRIGHT_CLI) install --dry-run chromium 2>/dev/null \
			| grep "Install location" | head -1 | sed 's/.*: *//') \
		&& if [ -d "$$INSTALL_DIR" ]; then \
			echo "Playwright chromium already installed at $$INSTALL_DIR — skipping install"; \
		else \
			node $(PLAYWRIGHT_CLI) install --with-deps chromium; \
		fi
