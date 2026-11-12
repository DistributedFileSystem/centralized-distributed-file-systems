'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const {blockersFromBody, isM2, candidate, shouldPromote} = require('./promote-ready.cjs');

const repo = 'DistributedFileSystem/centralized-distributed-file-systems';
const makeItem = (body, status = 'Backlog', milestone = 'M2 – Core features') => ({
  fieldValueByName: {name: status},
  content: {state: 'OPEN', repository: {nameWithOwner: repo}, milestone: {title: milestone}, body},
});

test('extracts only unique blockers from the dependency section', () => {
  const body = 'See #99\n## Phụ thuộc\n- Blocked by #10 — M1-07\n- Blocked by #13\n- Blocked by #10\n## Bàn giao\n- Blocked by #40';
  assert.deepEqual(blockersFromBody(body), [10, 13]);
  assert.deepEqual(blockersFromBody('No blockers listed, closes #10'), []);
});
test('supports English headings and CRLF', () => {
  assert.deepEqual(blockersFromBody('## Dependencies\r\n* Blocked by #25\r\n'), [25]);
});
test('accepts only milestone M2', () => {
  assert.equal(isM2('M2 – Core features'), true);
  for (const title of ['M1 – Setup', 'M3 – Release', 'M20', '', null]) assert.equal(isM2(title), false);
});
test('does not change any non-Backlog status or future milestone', () => {
  const body = '## Phụ thuộc\n- Blocked by #10';
  for (const status of ['Ready', 'In progress', 'Testing', 'In review', 'Done']) {
    assert.equal(candidate(makeItem(body, status), repo), false);
  }
  assert.equal(candidate(makeItem(body, 'Backlog', 'M3 – Release'), repo), false);
  assert.equal(candidate(makeItem('## Other\n- Blocked by #10'), repo), false);
});
test('requires every blocker to be completed', async () => {
  const item = makeItem('## Phụ thuộc\n- Blocked by #10\n- Blocked by #13');
  assert.equal(await shouldPromote(item, repo, async () => true), true);
  assert.equal(await shouldPromote(item, repo, async n => n === 10), false);
});
test('ignores different repositories and closed issues', async () => {
  const item = makeItem('## Phụ thuộc\n- Blocked by #10');
  assert.equal(await shouldPromote(item, 'other/repo', async () => true), false);
  item.content.state = 'CLOSED';
  assert.equal(await shouldPromote(item, repo, async () => true), false);
});
