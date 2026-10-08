// TwitchVault CDN Worker: Discord segment proxy with prefetch cache
// Route: GET /{messageId}/{attachmentId}
//
// Env vars (Workers dashboard > Settings > Variables):
//   DISCORD_USER_TOKEN, CHANNEL_ID
//   MESSAGES_LIMIT (default 50, max 50), MAX_PREFETCH (default 3)

const CACHE_TTL_SECONDS = 86400; // segments are immutable, so cache for 24h
const DISCORD_API = "https://discord.com/api/v10";
const CACHE_BASE = "https://twitchvault.cache";

// A missing env var would parse to NaN and silently disable prefetching.
const DEFAULT_MESSAGES_LIMIT = 50;
const DEFAULT_MAX_PREFETCH = 3;

// Candidates collected per prefetch slot, so already-cached entries can be skipped.
const PREFETCH_CANDIDATE_MULTIPLIER = 2;

export default {
  async fetch(request, env, ctx) {
    if (request.method === "OPTIONS") {
      return new Response(null, { headers: corsHeaders() });
    }

    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("Method Not Allowed", { status: 405, headers: corsHeaders() });
    }

    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean);

    if (parts.length !== 2) {
      return new Response(
        "Bad Request: expected /{messageId}/{attachmentId}",
        { status: 400, headers: corsHeaders() }
      );
    }

    const [messageId, attachmentId] = parts;
    const channelId = env.CHANNEL_ID;
    const token = env.DISCORD_USER_TOKEN;
    const limit = parseInt(env.MESSAGES_LIMIT) || DEFAULT_MESSAGES_LIMIT;
    const maxPrefetch = parseInt(env.MAX_PREFETCH) || DEFAULT_MAX_PREFETCH;

    const cache = caches.default;
    const cacheKey = segmentCacheKey(attachmentId);

    const cached = await cache.match(cacheKey);
    if (cached) {
      // Cache API responses have immutable headers, so wrap before modifying.
      const hit = new Response(cached.body, cached);
      hit.headers.set("X-Segment-Cache", "HIT");
      applyCorsHeaders(hit.headers);
      return hit;
    }

    const apiResp = await fetch(
      `${DISCORD_API}/channels/${channelId}/messages?around=${messageId}&limit=${limit}`,
      { headers: { Authorization: `${token}` } }
    );

    if (!apiResp.ok) {
      return new Response(`Discord API error: ${apiResp.status}`, {
        status: apiResp.status,
        headers: corsHeaders(),
      });
    }

    const messages = await apiResp.json();

    const targetMsgIndex = messages.findIndex(m => m.id === messageId);
    if (targetMsgIndex === -1) {
      return new Response("Message not found in response", {
        status: 404,
        headers: corsHeaders(),
      });
    }

    const targetMsg = messages[targetMsgIndex];
    const targetAttachIndex = targetMsg.attachments.findIndex(a => a.id === attachmentId);
    if (targetAttachIndex === -1) {
      return new Response("Attachment not found in message", {
        status: 404,
        headers: corsHeaders(),
      });
    }

    // Dispatch prefetch before the main fetch so both run concurrently.
    const prefetchQueue = [];
    const candidateCap = maxPrefetch * PREFETCH_CANDIDATE_MULTIPLIER;

    outer:
    for (let mi = targetMsgIndex; mi >= 0; mi--) {
      const attachments = messages[mi].attachments ?? [];
      const startIdx = mi === targetMsgIndex ? targetAttachIndex + 1 : 0;

      for (let ai = startIdx; ai < attachments.length; ai++) {
        prefetchQueue.push({
          attachmentId: attachments[ai].id,
          cdnUrl: attachments[ai].url,
        });
        if (prefetchQueue.length >= candidateCap) break outer;
      }
    }

    ctx.waitUntil(prefetch(prefetchQueue, cache, maxPrefetch, CACHE_TTL_SECONDS));

    const segResp = await fetch(targetMsg.attachments[targetAttachIndex].url, {
      method: request.method,
    });

    if (!segResp.ok) {
      const response = new Response(segResp.body, segResp);
      applyCorsHeaders(response.headers);
      return response;
    }

    const response = new Response(segResp.body, segResp);

    response.headers.set(
      "Cache-Control",
      `public, max-age=${CACHE_TTL_SECONDS}`
    );
    response.headers.set("X-Segment-Cache", "MISS");
    applyCorsHeaders(response.headers);

    ctx.waitUntil(cache.put(cacheKey, response.clone()));

    return response;
  },
};

async function prefetch(queue, cache, maxPrefetch, cacheTtl) {
  if (queue.length === 0) return;

  const withKeys = queue.map(entry => ({
    entry,
    key: segmentCacheKey(entry.attachmentId),
  }));
  const hits = await Promise.all(withKeys.map(({ key }) => cache.match(key)));

  const toFetch = [];
  for (let i = 0; i < withKeys.length && toFetch.length < maxPrefetch; i++) {
    if (!hits[i]) toFetch.push(withKeys[i]);
  }

  if (toFetch.length === 0) return;

  await Promise.all(toFetch.map(async ({ entry, key }) => {
    try {
      const resp = await fetch(entry.cdnUrl);
      if (!resp.ok) return;

      const response = new Response(resp.body, resp);
      response.headers.set("Cache-Control", `public, max-age=${cacheTtl}`);
      response.headers.set("X-Segment-Cache", "MISS");
      await cache.put(key, response);
    } catch {
      // Non-critical: the player gets a normal MISS and retries.
    }
  }));
}

function segmentCacheKey(attachmentId) {
  return new Request(`${CACHE_BASE}/${attachmentId}`, { method: "GET" });
}

function corsHeaders() {
  const headers = new Headers();
  applyCorsHeaders(headers);
  return headers;
}

function applyCorsHeaders(headers) {
  headers.set("Access-Control-Allow-Origin", "*");
  headers.set("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS");
  headers.set("Access-Control-Allow-Headers", "Range");
  headers.set("Accept-Ranges", "bytes");
}