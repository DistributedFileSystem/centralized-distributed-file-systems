'use strict';

// Stage-gated project automation: only the first unfinished milestone can enter Ready.
// Respect current work; only move open Backlog issues after their blockers are complete.
const API = 'https://api.github.com';

function blockersFromBody(body) {
  const section = /(?:^|\n)##\s*(?:Phụ thuộc|Dependencies)\s*\r?\n([\s\S]*?)(?=\r?\n##\s+|$)/i.exec(body || '');
  if (!section) return [];
  return [...new Set([...section[1].matchAll(/^\s*[-*]\s*Blocked by\s+#(\d+)\b/gim)]
    .map(match => Number(match[1])))];
}

const STAGE_ORDER = Object.freeze(['M1', 'M2', 'M3', 'S1', 'S2', 'S3']);

function stageCode(title) {
  const match = /^(M[1-3]|S[1-3])(?:\s|[-–—]|$)/i.exec(title || '');
  return match ? match[1].toUpperCase() : null;
}

// Never unlock a later stage until every issue in the earlier stage is closed.
// An empty stage without any completed issues is not silently skipped.
function activeStageFromMilestones(milestones) {
  const stages = new Map();
  for (const milestone of milestones) {
    const stage = stageCode(milestone.title);
    if (!stage) continue;
    if (stages.has(stage)) throw new Error('Duplicate milestone for stage ' + stage);
    stages.set(stage, milestone);
  }
  for (const stage of STAGE_ORDER) {
    const milestone = stages.get(stage);
    if (!milestone) throw new Error('Missing milestone for stage ' + stage);
    if (!Number.isInteger(milestone.open_issues) || !Number.isInteger(milestone.closed_issues)) {
      throw new Error('Missing issue counts for milestone ' + milestone.title);
    }
    if (milestone.open_issues > 0 || milestone.closed_issues === 0) return stage;
  }
  return null;
}

function candidate(item, repository, activeStage) {
  const issue = item?.content;
  if (!activeStage || !issue || issue.repository?.nameWithOwner?.toLowerCase() !== repository.toLowerCase()) return false;
  if (issue.state !== 'OPEN' || stageCode(issue.milestone?.title) !== activeStage) return false;
  if (item.fieldValueByName?.name?.toLowerCase() !== 'backlog') return false;
  return true;
}

async function shouldPromote(item, repository, activeStage, blockerCompleted) {
  if (!candidate(item, repository, activeStage)) return false;
  for (const number of blockersFromBody(item.content.body)) {
    if (!(await blockerCompleted(number))) return false;
  }
  return true;
}

async function api(path, token, options = {}) {
  const response = await fetch(API + path, {
    ...options,
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: 'Bearer ' + token,
      'X-GitHub-Api-Version': '2022-11-28',
      ...(options.body ? {'Content-Type': 'application/json'} : {}),
    },
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error('GitHub API ' + path + ': HTTP ' + response.status + ': ' + (data.message || 'request failed'));
  return data;
}

async function graphql(query, variables, token) {
  const result = await api('/graphql', token, {
    method: 'POST',
    body: JSON.stringify({ query, variables }),
  });
  if (result.errors?.length) {
    throw new Error('GitHub GraphQL: ' + result.errors.map(error => error.message).join('; '));
  }
  return result.data;
}

async function getMilestones(repository, token) {
  const milestones = [];
  for (let page = 1; page <= 20; page++) {
    const batch = await api('/repos/' + repository + '/milestones?state=all&per_page=100&page=' + page, token);
    if (!Array.isArray(batch)) throw new Error('Could not read repository milestones.');
    milestones.push(...batch);
    if (batch.length < 100) return milestones;
  }
  throw new Error('Milestone list exceeded pagination limit.');
}

async function findProject(org, title, token) {
  const query = 'query($org:String!,$after:String){organization(login:$org){projectsV2(first:100,after:$after){pageInfo{hasNextPage endCursor}nodes{id number title}}}}';
  let after = null;
  do {
    const data = await graphql(query, {org, after}, token);
    const projects = data.organization?.projectsV2;
    if (!projects) throw new Error('Cannot read organization Projects for ' + org + '. Check token permissions.');
    const match = projects.nodes.find(project => project.title === title);
    if (match) return match;
    if (!projects.pageInfo.hasNextPage) break;
    after = projects.pageInfo.endCursor;
  } while (after);
  throw new Error('Project "' + title + '" not found under organization ' + org + '.');
}

async function getProjectItems(org, projectNumber, token) {
  const query = 'query($org:String!,$number:Int!,$after:String){organization(login:$org){projectV2(number:$number){id field(name:"Status"){... on ProjectV2SingleSelectField{id options{id name}}}items(first:100,after:$after){pageInfo{hasNextPage endCursor}nodes{id content{... on Issue{number title body state milestone{title}repository{nameWithOwner}}}fieldValueByName(name:"Status"){... on ProjectV2ItemFieldSingleSelectValue{name}}}}}}}';
  let after = null;
  let field = null;
  const items = [];
  do {
    const data = await graphql(query, {org, number: projectNumber, after}, token);
    const project = data.organization?.projectV2;
    if (!project) throw new Error('Project #' + projectNumber + ' not accessible.');
    field = project.field;
    items.push(...project.items.nodes);
    if (!project.items.pageInfo.hasNextPage) break;
    after = project.items.pageInfo.endCursor;
  } while (after);
  return {field, items};
}

async function promoteItem(projectId, itemId, fieldId, optionId, token) {
  const mutation = 'mutation($projectId:ID!,$itemId:ID!,$fieldId:ID!,$optionId:String!){updateProjectV2ItemFieldValue(input:{projectId:$projectId itemId:$itemId fieldId:$fieldId value:{singleSelectOptionId:$optionId}}){projectV2Item{id}}}';
  await graphql(mutation, {projectId, itemId, fieldId, optionId}, token);
}

async function main() {
  const token = process.env.GH_TOKEN;
  const repo = process.env.GH_REPOSITORY;
  const org = process.env.GH_ORGANIZATION;
  const projectTitle = process.env.PROJECT_TITLE || 'DFS - Task board';
  if (!token) throw new Error('Missing DFS_PROJECTS_TOKEN secret. See docs/automation.md.');
  if (!repo || !org) throw new Error('Missing GH_REPOSITORY or GH_ORGANIZATION.');

  const activeStage = activeStageFromMilestones(await getMilestones(repo, token));
  if (!activeStage) {
    console.log('All planned stages are complete; no issues to move.');
    return;
  }
  console.log('Active stage: ' + activeStage + ' (later stages remain locked).');

  const project = await findProject(org, projectTitle, token);
  const {field, items} = await getProjectItems(org, project.number, token);
  const ready = field?.options?.find(option => option.name.toLowerCase() === 'ready');
  const backlog = field?.options?.find(option => option.name.toLowerCase() === 'backlog');
  if (!field?.id || !ready || !backlog) {
    throw new Error('The project must have a Status single-select field with Backlog and Ready options.');
  }

  const cache = new Map();
  async function blockerCompleted(number) {
    if (!cache.has(number)) {
      const issue = await api('/repos/' + repo + '/issues/' + number, token);
      cache.set(number, issue.state === 'closed' && issue.state_reason !== 'not_planned');
    }
    return cache.get(number);
  }

  let changed = 0;
  for (const item of items) {
    if (await shouldPromote(item, repo, activeStage, blockerCompleted)) {
      await promoteItem(project.id, item.id, field.id, ready.id, token);
      console.log('Moved ' + activeStage + ' #' + item.content.number + ' to Ready: ' + item.content.title);
      changed += 1;
    }
  }
  console.log('Done: inspected ' + items.length + ' project items; moved ' + changed + ' ' + activeStage + ' issue(s).');
}

if (require.main === module) {
  main().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
  });
}

module.exports = {blockersFromBody, stageCode, activeStageFromMilestones, candidate, shouldPromote};
