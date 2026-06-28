/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  // Produce a self-contained build for the Docker image
  output: 'standalone',
  eslint: {
    ignoreDuringBuilds: true,
  },
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
        {
          source: '/api/auth/:path*',
          destination: `${backendBase}/api/auth/:path*`,
        },
      ],
    }
  },
}

export default nextConfig
