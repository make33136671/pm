import { api } from '#/api/request';

// Repository
export async function getRepoList(params: any) {
  return api.get('/GitRepository/List', { params });
}

export async function getRepoDetail(id: number) {
  return api.get('/GitRepository/Detail', { params: { id } });
}

export async function postRepoAdd(data: any) {
  return api.post('/GitRepository/Add', data);
}

export async function postRepoEdit(data: any) {
  if (data.id) {
    return api.post('/GitRepository/Edit', data);
  }
  return postRepoAdd(data);
}

export async function postRepoDel(id: number) {
  return api.post('/GitRepository/Del', { id });
}

export async function postRepoSync(id: number) {
  return api.post('/GitRepository/Sync', { id });
}

export async function getRepoBranches(id: number) {
  return api.get('/GitRepository/Branches', { params: { id } });
}

export async function getRepoCommits(id: number, branch?: string) {
  return api.get('/GitRepository/Commits', { params: { id, branch } });
}

// Pipeline
export async function getPipelineList(params: any) {
  return api.get('/GitPipeline/List', { params });
}

export async function getPipelineDetail(id: number) {
  return api.get('/GitPipeline/Detail', { params: { id } });
}
