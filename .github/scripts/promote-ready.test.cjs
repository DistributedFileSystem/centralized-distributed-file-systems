'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const {blockersFromBody, stageCode, activeStageFromMilestones, candidate, shouldPromote} = require('./promote-ready.cjs');

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
test('accepts only valid milestone codes', () => {
  for (const code of ['M1', 'M2', 'M3', 'S1', 'S2', 'S3']) {
    assert.equal(stageCode(code + ' – work'), code);
  }
  for (const title of ['M20', 'M4', 'S4', '', null]) assert.equal(stageCode(title), null);
});
test('does not change any non-Backlog status or future milestone', () => {
  const body = '## Phụ thuộc\n- Blocked by #10';
  for (const status of ['Ready', 'In progress', 'Testing', 'In review', 'Done']) {
    assert.equal(candidate(makeItem(body, status), repo, 'M2'), false);
  }
  assert.equal(candidate(makeItem(body, 'Backlog', 'M3 – Release'), repo, 'M2'), false);
  assert.equal(candidate(makeItem('## Other\n- Blocked by #10'), repo, 'M2'), true);
});
test('requires every blocker to be completed', async () => {
  const item = makeItem('## Phụ thuộc\n- Blocked by #10\n- Blocked by #13');
  assert.equal(await shouldPromote(item, repo, 'M2', async () => true), true);
  assert.equal(await shouldPromote(item, repo, 'M2', async n => n === 10), false);
});
test('ignores different repositories and closed issues', async () => {
  const item = makeItem('## Phụ thuộc\n- Blocked by #10');
  assert.equal(await shouldPromote(item, 'other/repo', 'M2', async () => true), false);
  item.content.state = 'CLOSED';
  assert.equal(await shouldPromote(item, repo, 'M2', async () => true), false);
});

const ORDER = ['M1', 'M2', 'M3', 'S1', 'S2', 'S3'];
function milestones(openByStage = {}) {
  return ORDER.map(stage => ({
    title: stage + ' – Work',
    open_issues: openByStage[stage] ?? 4,
    closed_issues: stage === 'M1' ? 12 : 0,
  }));
}
test('M2 becomes active after M1 finishes; M3 remains locked', () => {
  assert.equal(activeStageFromMilestones(milestones({M1: 0, M2: 15})), 'M2');
});
test('does not unlock next stage until current stage is fully closed', () => {
  assert.equal(activeStageFromMilestones(milestones({M1: 1, M2: 0})), 'M1');
  const completed = milestones({M1: 0, M2: 0});
  completed[1].closed_issues = 15;
  assert.equal(activeStageFromMilestones(completed), 'M3');
});
test('does not skip empty or missing milestones', () => {
  assert.equal(activeStageFromMilestones(milestones({M1: 0, M2: 0})), 'M2');
  assert.throws(() => activeStageFromMilestones(milestones().slice(1)), /Missing milestone/);
});
test('reports all completed stages without promoting more issues', () => {
  const completed = milestones(Object.fromEntries(ORDER.map(stage => [stage, 0])));
  for (const milestone of completed) milestone.closed_issues = 4;
  assert.equal(activeStageFromMilestones(completed), null);
});
test('no blockers means immediate Ready only for active stage', async () => {
  const item = makeItem('## Phụ thuộc\n\n## Bàn giao');
  assert.equal(await shouldPromote(item, repo, 'M2', async () => false), true);
  assert.equal(await shouldPromote(item, repo, 'M3', async () => true), false);
});
