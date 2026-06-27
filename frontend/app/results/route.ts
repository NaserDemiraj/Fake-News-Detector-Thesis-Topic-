import { readFile } from 'fs/promises'
import { join } from 'path'
import { NextResponse } from 'next/server'

export async function GET() {
  try {
    const filePath = join(process.cwd(), 'public', 'results.html')
    const html = await readFile(filePath, 'utf-8')
    return new NextResponse(html, {
      headers: { 'Content-Type': 'text/html' }
    })
  } catch (error) {
    return new NextResponse('404 - Results page not found', { status: 404 })
  }
}
