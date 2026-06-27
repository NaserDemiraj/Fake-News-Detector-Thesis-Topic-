/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  typescript: {
    ignoreBuildErrors: true,
  },
  images: {
    unoptimized: true,
  },
  async rewrites() {
    const backendBase = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000'
    return {
      beforeFiles: [
        {
          source: '/api/Analysis',
          destination: `${backendBase}/api/Analysis`,
        },
        {
          source: '/api/Analysis/:path*',
          destination: `${backendBase}/api/Analysis/:path*`,
        },
      ],
    }
  },
}

export default nextConfig
