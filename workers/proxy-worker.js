export default {
  async fetch(request) {
    const target = new URL(request.url).searchParams.get('url');

    if (!target) {
      return new Response('Missing "url" query parameter', { status: 400 });
    }

    let targetUrl;
    try {
      targetUrl = new URL(target);
    } catch {
      return new Response('Invalid "url" query parameter', { status: 400 });
    }

    const hasBody = request.method !== 'GET' && request.method !== 'HEAD';

    const headers = new Headers(request.headers);
    headers.set('Accept-Encoding', 'identity'); // stop fetch() from auto-decompressing, so headers/body stay in sync

    const upstream = await fetch(targetUrl.toString(), {
      method: request.method,
      headers,
      body: hasBody ? request.body : undefined,
      duplex: hasBody ? 'half' : undefined,
      redirect: 'manual',
    });

    return new Response(upstream.body, {
      status: upstream.status,
      statusText: upstream.statusText,
      headers: upstream.headers,
    });
  },
};