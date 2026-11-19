import type {
  FullConfig,
  FullResult,
  Reporter,
  Suite,
  TestCase,
  TestResult,
} from '@playwright/test/reporter';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

/** Deliberately omit Playwright errors, call logs, stdout, DOM, and attachments. */
export default class SafeReporter implements Reporter {
  private readonly results: {
    status: string;
    durationMs: number;
    checks: string[];
    skippedChecks: string[];
    failedStage: string | null;
    failureKind: string | null;
    platformHttpError: number | null;
  }[] = [];
  private infrastructureError = false;
  private readonly listOnly = process.argv.includes('--list');
  printsToStdio(): boolean {
    return true;
  }
  onBegin(_config: FullConfig, suite: Suite): void {
    if (this.listOnly)
      process.stdout.write(
        `Phase 1 browser acceptance discovery: ${suite.allTests().length} test(s). No browser checks executed.\n`,
      );
  }
  onError(): void {
    this.infrastructureError = true;
  }
  onTestEnd(test: TestCase, result: TestResult): void {
    const safe = (type: string) =>
      test.annotations
        .filter((item) => item.type === type && /^[a-z-]+$/.test(item.description ?? ''))
        .map((item) => item.description!);
    this.results.push({
      status: result.status,
      durationMs: result.duration,
      checks: safe('acceptance'),
      skippedChecks: safe('skipped-check'),
      failedStage: safe('failed-stage')[0] ?? null,
      failureKind: safe('failure-kind')[0] ?? null,
      platformHttpError:
        Number(
          test.annotations.find(
            (item) => item.type === 'platform-http-error' && /^\d{3}$/.test(item.description ?? ''),
          )?.description,
        ) || null,
    });
  }
  onEnd(result: FullResult): void {
    if (this.listOnly) return;
    const passed = this.results.filter((item) => item.status === 'passed').length;
    const failed = this.results.length - passed;
    const directory = resolve('test-results');
    mkdirSync(directory, { recursive: true });
    writeFileSync(
      resolve(directory, 'phase-1-browser-summary.json'),
      JSON.stringify(
        {
          status: result.status,
          infrastructureError: this.infrastructureError,
          passed,
          failed,
          results: this.results,
        },
        null,
        2,
      ) + '\n',
    );
    process.stdout.write(
      `Phase 1 browser acceptance: ${passed} passed, ${failed} failed; overall ${result.status}.\n`,
    );
    for (const item of this.results)
      if (item.failedStage)
        process.stdout.write(
          `Failed stage: ${item.failedStage}. Sensitive diagnostics suppressed.\n`,
        );
    if (this.infrastructureError)
      process.stdout.write(
        'Browser test infrastructure failed. Check the credential file, browser installation, and HTTPS demo availability.\n',
      );
  }
}
