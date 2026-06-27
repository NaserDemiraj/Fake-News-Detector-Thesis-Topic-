import { NextRequest, NextResponse } from "next/server"

export async function GET(request: NextRequest) {
  const searchParams = request.nextUrl.searchParams
  const url = searchParams.get("url")

  if (!url) {
    return NextResponse.json({ error: "URL parameter required" }, { status: 400 })
  }

  try {
    const urlObj = new URL(url)
    const domain = urlObj.hostname

    // Try to fetch the page and extract metadata
    const response = await fetch(url, {
      headers: {
        "User-Agent":
          "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
      },
      timeout: 5000,
    })

    if (!response.ok) {
      throw new Error("Failed to fetch URL")
    }

    const html = await response.text()

    // Extract title from og:title or title tag
    let title = domain
    const titleMatch = html.match(/<title[^>]*>([^<]+)<\/title>/i)
    if (titleMatch) {
      title = titleMatch[1].trim()
    } else {
      const ogTitleMatch = html.match(/<meta[^>]*property="og:title"[^>]*content="([^"]+)"/i)
      if (ogTitleMatch) {
        title = ogTitleMatch[1]
      }
    }

    // Get favicon
    const faviconUrl = `https://www.google.com/s2/favicons?domain=${domain}&sz=128`

    return NextResponse.json({
      title: title.substring(0, 60),
      domain,
      favicon: faviconUrl,
    })
  } catch (error) {
    console.error("Error fetching preview:", error)

    // Return fallback data
    try {
      const urlObj = new URL(url)
      return NextResponse.json({
        title: urlObj.hostname,
        domain: urlObj.hostname,
        favicon: `https://www.google.com/s2/favicons?domain=${urlObj.hostname}&sz=128`,
      })
    } catch {
      return NextResponse.json({ error: "Invalid URL" }, { status: 400 })
    }
  }
}
