import worker from './worker.js';
export default worker;
export const { fetch, verifyEdgeToken, handleRequest } = worker;
