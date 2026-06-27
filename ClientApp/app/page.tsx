"use client"

import { useState, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Progress } from "@/components/ui/progress"
import { Badge } from "@/components/ui/badge"
import { Separator } from "@/components/ui/separator"
import {
  CheckCircle,
  AlertTriangle,
  AlertCircle,
  Newspaper,
  LinkIcon,
  FileText,
  Info,
  BarChart3,
  Shield,
  History,
  BookOpen,
  Search,
  Lightbulb,
  ExternalLink,
  ChevronRight,
  Loader2,
  Save,
  Share2,
  Download,
  Bug,
} from "lucide-react"

// Sample tips for identifying fake news
const fakeTips = [
  "Check the source's credibility and reputation",
  "Look for unusual URLs or site names",
  "Check for poor spelling and grammar",
  "Look for excessive punctuation or ALL CAPS",
  "Verify if other reputable sources are reporting the same news",
]

// Sample recent analyses for fallback mode
const sampleRecentAnalyses = [
  {
    id: "1",
    title: "Climate Change Report",
    url: "https://example.com/climate-report",
    score: 85,
    verdict: "Likely Legitimate",
    relativeDate: "2 hours ago",
  },
  {
    id: "2",
    title: "Political Scandal Exposed",
    url: "https://example.com/political-scandal",
    score: 32,
    verdict: "Potentially Fake News",
    relativeDate: "Yesterday",
  },
  {
    id: "3",
    title: "Health Benefits of Coffee",
    url: "https://example.com/coffee-benefits",
    score: 76,
    verdict: "Likely Legitimate",
    relativeDate: "2 days ago",
  },
]

// Sample stats for fallback mode
const sampleStats = {
  totalAnalyses: 124,
  fakeNewsDetected: 37,
  averageScore: 62.4,
}

// Generate mock analysis result for fallback mode
const generateMockAnalysisResult = (content) => {
  // Simple algorithm to generate a somewhat realistic score based on content
  const contentLength = content.length
  let baseScore = 50 // Start with a neutral score

  // Longer content tends to be more legitimate (very simple heuristic)
  if (contentLength > 500) baseScore += 20
  else if (contentLength > 200) baseScore += 10

  // Check for some keywords that might indicate credibility
  const credibilityTerms = ["according to", "research", "study", "evidence", "expert"]
  credibilityTerms.forEach((term) => {
    if (content.toLowerCase().includes(term)) baseScore += 5
  })

  // Check for some red flags
  const redFlagTerms = ["shocking", "you won't believe", "secret", "conspiracy", "!!!"]
  redFlagTerms.forEach((term) => {
    if (content.toLowerCase().includes(term)) baseScore -= 10
  })

  // Add some randomness
  const randomFactor = Math.floor(Math.random() * 20) - 10 // -10 to +10
  const finalScore = Math.max(0, Math.min(100, baseScore + randomFactor))

  // Generate factors with somewhat correlated scores
  const factors = [
    { name: "Source Credibility", score: finalScore + (Math.random() * 20 - 10) },
    { name: "Emotional Language", score: finalScore + (Math.random() * 20 - 10) },
    { name: "Fact Consistency", score: finalScore + (Math.random() * 20 - 10) },
    { name: "Clickbait Detection", score: finalScore + (Math.random() * 20 - 10) },
    { name: "Conspiracy Theories", score: finalScore + (Math.random() * 20 - 10) },
  ]

  // Ensure all scores are within 0-100 range
  factors.forEach((factor) => {
    factor.score = Math.max(0, Math.min(100, factor.score))
  })

  // Determine verdict and explanation
  let verdict, explanation
  if (finalScore > 70) {
    verdict = "Likely Legitimate"
    explanation =
      "This content appears to be from a credible source and contains balanced reporting with factual information."
  } else if (finalScore > 40) {
    verdict = "Potentially Misleading"
    explanation = "This content contains some questionable claims and may use emotional language or clickbait tactics."
  } else {
    verdict = "Potentially Fake News"
    explanation =
      "This content contains multiple red flags including excessive emotional language, clickbait tactics, and conspiracy theories."
  }

  return {
    success: true,
    score: finalScore,
    verdict,
    explanation,
    factors: factors.map((f) => ({ name: f.name, score: Math.round(f.score * 10) / 10 })),
  }
}

export default function FakeNewsDetector() {
  const [url, setUrl] = useState("")
  const [text, setText] = useState("")
  const [isAnalyzing, setIsAnalyzing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [activeTab, setActiveTab] = useState<string>("dashboard")
  const [recentAnalyses, setRecentAnalyses] = useState(sampleRecentAnalyses)
  const [stats, setStats] = useState(sampleStats)
  const [showSaveDialog, setShowSaveDialog] = useState(false)
  const [showShareDialog, setShowShareDialog] = useState(false)
  const [showAboutDialog, setShowAboutDialog] = useState(false)
  const [showDebugDialog, setShowDebugDialog] = useState(false)
  const [debugInfo, setDebugInfo] = useState<any>({})
  const [isUsingFallback, setIsUsingFallback] = useState(false)
  const [result, setResult] = useState<null | {
    success: boolean
    score: number
    verdict: string
    explanation: string
    factors: { name: string; score: number }[]
  }>(null)

  // Fetch recent analyses and stats when the component mounts
  useEffect(() => {
    fetchRecentAnalyses()
    fetchStats()
  }, [])

  const fetchRecentAnalyses = async () => {
    try {
      // Try multiple possible API endpoints
      const possibleEndpoints = [
        "/api/Analysis/recent",
        "http://localhost:5000/api/Analysis/recent",
        "https://localhost:7126/api/Analysis/recent",
      ]

      let response = null
      const errorDetails = []

      for (const endpoint of possibleEndpoints) {
        try {
          console.log(`Trying to fetch recent analyses from: ${endpoint}`)
          response = await fetch(endpoint, {
            method: "GET",
            headers: { "Content-Type": "application/json" },
            // Add a timeout to prevent long waits
            signal: AbortSignal.timeout(5000),
          })

          if (response.ok) {
            console.log(`Successfully fetched from: ${endpoint}`)
            break
          } else {
            errorDetails.push(`${endpoint}: ${response.status} ${response.statusText}`)
          }
        } catch (err) {
          errorDetails.push(`${endpoint}: ${err.message}`)
        }
      }

      if (response && response.ok) {
        const data = await response.json()
        setRecentAnalyses(data)
        setIsUsingFallback(false)
      } else {
        console.warn("Using fallback data for recent analyses. Errors:", errorDetails)
        setDebugInfo((prev) => ({ ...prev, recentAnalysesErrors: errorDetails }))
        setIsUsingFallback(true)
      }
    } catch (error) {
      console.warn("Error fetching recent analyses, using fallback data:", error)
      setDebugInfo((prev) => ({ ...prev, recentAnalysesError: error.toString() }))
      setIsUsingFallback(true)
    }
  }

  const fetchStats = async () => {
    try {
      // Try multiple possible API endpoints
      const possibleEndpoints = [
        "/api/Analysis/stats",
        "http://localhost:5000/api/Analysis/stats",
        "https://localhost:7126/api/Analysis/stats",
      ]

      let response = null
      const errorDetails = []

      for (const endpoint of possibleEndpoints) {
        try {
          console.log(`Trying to fetch stats from: ${endpoint}`)
          response = await fetch(endpoint, {
            method: "GET",
            headers: { "Content-Type": "application/json" },
            // Add a timeout to prevent long waits
            signal: AbortSignal.timeout(5000),
          })

          if (response.ok) {
            console.log(`Successfully fetched from: ${endpoint}`)
            break
          } else {
            errorDetails.push(`${endpoint}: ${response.status} ${response.statusText}`)
          }
        } catch (err) {
          errorDetails.push(`${endpoint}: ${err.message}`)
        }
      }

      if (response && response.ok) {
        const data = await response.json()
        setStats({
          totalAnalyses: data.totalAnalyses,
          fakeNewsDetected: data.fakeNewsDetected,
          averageScore: data.averageScore,
        })
        setIsUsingFallback(false)
      } else {
        console.warn("Using fallback data for stats. Errors:", errorDetails)
        setDebugInfo((prev) => ({ ...prev, statsErrors: errorDetails }))
        setIsUsingFallback(true)
      }
    } catch (error) {
      console.warn("Error fetching stats, using fallback data:", error)
      setDebugInfo((prev) => ({ ...prev, statsError: error.toString() }))
      setIsUsingFallback(true)
    }
  }

  const analyzeNews = async (type: "url" | "text") => {
    setIsAnalyzing(true)
    setError(null)
    setActiveTab("results")

    const content = type === "url" ? url : text

    try {
      // Try multiple possible API endpoints
      const possibleEndpoints = [
        "/api/Analysis",
        "http://localhost:5000/api/Analysis",
        "https://localhost:7126/api/Analysis",
      ]

      let response = null
      const errorDetails = []

      for (const endpoint of possibleEndpoints) {
        try {
          console.log(`Trying to analyze content using: ${endpoint}`)
          response = await fetch(endpoint, {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
            },
            body: JSON.stringify({
              type,
              content,
            }),
            // Add a timeout to prevent long waits
            signal: AbortSignal.timeout(10000),
          })

          if (response.ok) {
            console.log(`Successfully analyzed using: ${endpoint}`)
            break
          } else {
            const errorText = await response.text()
            errorDetails.push(`${endpoint}: ${response.status} ${response.statusText} - ${errorText}`)
          }
        } catch (err) {
          errorDetails.push(`${endpoint}: ${err.message}`)
        }
      }

      if (response && response.ok) {
        const data = await response.json()
        setResult(data)
        setIsUsingFallback(false)

        // Refresh recent analyses and stats
        fetchRecentAnalyses()
        fetchStats()
      } else {
        // If API call fails, use fallback mode
        console.warn("API call failed, using fallback mode. Errors:", errorDetails)
        setDebugInfo((prev) => ({ ...prev, analyzeErrors: errorDetails }))
        setIsUsingFallback(true)

        // Generate mock result
        const mockResult = generateMockAnalysisResult(content)
        setResult(mockResult)

        // Add to recent analyses
        const newAnalysis = {
          id: Date.now().toString(),
          title: type === "url" ? (url.startsWith("http") ? new URL(url).hostname : url) : "Text Analysis",
          url: type === "url" ? url : "",
          score: mockResult.score,
          verdict: mockResult.verdict,
          relativeDate: "Just now",
        }

        setRecentAnalyses([newAnalysis, ...recentAnalyses.slice(0, 9)])
      }
    } catch (error) {
      console.warn("Error analyzing news, using fallback mode:", error)
      setDebugInfo((prev) => ({ ...prev, analyzeError: error.toString() }))
      setIsUsingFallback(true)

      // Generate mock result
      const mockResult = generateMockAnalysisResult(content)
      setResult(mockResult)

      // Add to recent analyses
      const newAnalysis = {
        id: Date.now().toString(),
        title: type === "url" ? (url.startsWith("http") ? new URL(url).hostname : url) : "Text Analysis",
        url: type === "url" ? url : "",
        score: mockResult.score,
        verdict: mockResult.verdict,
        relativeDate: "Just now",
      }

      setRecentAnalyses([newAnalysis, ...recentAnalyses.slice(0, 9)])
    } finally {
      setIsAnalyzing(false)
    }
  }

  const saveResult = () => {
    // In a real app, this would save to a database
    setShowSaveDialog(true)
    setTimeout(() => setShowSaveDialog(false), 2000)

    // Add to recent analyses if using fallback mode
    if (isUsingFallback && result) {
      const newAnalysis = {
        id: Date.now().toString(),
        title: url ? (url.startsWith("http") ? new URL(url).hostname : url) : "Text Analysis",
        url: url || "",
        score: result.score,
        verdict: result.verdict,
        relativeDate: "Just now",
      }

      setRecentAnalyses([newAnalysis, ...recentAnalyses.slice(0, 9)])
    }
  }

  const shareResult = () => {
    // In a real app, this would generate a shareable link
    setShowShareDialog(true)
    setTimeout(() => setShowShareDialog(false), 2000)
  }

  const viewAllAnalyses = () => {
    // In a real app, this would navigate to a page with all analyses
    alert("This would show all analyses in a real application")
  }

  const showHistory = () => {
    // In a real app, this would navigate to the history page
    setActiveTab("dashboard")
  }

  const getStatusIcon = (score: number) => {
    if (score > 70) return <CheckCircle className="h-6 w-6 text-green-500" />
    if (score > 40) return <AlertTriangle className="h-6 w-6 text-yellow-500" />
    return <AlertCircle className="h-6 w-6 text-red-500" />
  }

  const getStatusColor = (score: number) => {
    if (score > 70) return "bg-green-500"
    if (score > 40) return "bg-yellow-500"
    return "bg-red-500"
  }

  const getStatusText = (score: number) => {
    if (score > 70) return "text-green-500"
    if (score > 40) return "text-yellow-500"
    return "text-red-500"
  }

  const getStatusBg = (score: number) => {
    if (score > 70) return "bg-green-50"
    if (score > 40) return "bg-yellow-50"
    return "bg-red-50"
  }

  const getStatusBorder = (score: number) => {
    if (score > 70) return "border-green-200"
    if (score > 40) return "border-yellow-200"
    return "border-red-200"
  }

  return (
    <div className="min-h-screen bg-gradient-to-b from-gray-50 to-gray-100 dark:from-gray-900 dark:to-gray-800">
      {isUsingFallback && (
        <div className="bg-amber-50 border-b border-amber-200 p-2 text-center text-amber-800 dark:bg-amber-900 dark:border-amber-800 dark:text-amber-200">
          <p className="text-sm">
            <AlertTriangle className="inline-block h-4 w-4 mr-1" />
            Running in fallback mode with simulated data. Backend connection failed.
            <Button
              variant="link"
              size="sm"
              className="text-amber-800 dark:text-amber-200 underline ml-2"
              onClick={() => setShowDebugDialog(true)}
            >
              <Bug className="h-3 w-3 mr-1" />
              Debug Info
            </Button>
          </p>
        </div>
      )}

      <header className="bg-white dark:bg-gray-950 border-b border-gray-200 dark:border-gray-800 sticky top-0 z-10">
        <div className="container mx-auto px-4 py-3 flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <Shield className="h-6 w-6 text-blue-600 dark:text-blue-400" />
            <h1 className="text-xl font-bold text-gray-900 dark:text-white">Fake News Detector</h1>
          </div>
          <div className="flex items-center space-x-4">
            <Button variant="outline" size="sm" onClick={() => setShowAboutDialog(true)}>
              <Info className="h-4 w-4 mr-2" />
              About
            </Button>
            <Button variant="ghost" size="sm" onClick={showHistory}>
              <History className="h-4 w-4 mr-2" />
              History
            </Button>
          </div>
        </div>
      </header>

      <main className="container mx-auto py-8 px-4">
        <div className="grid grid-cols-1 lg:grid-cols-4 gap-6">
          {/* Sidebar */}
          <div className="lg:col-span-1">
            <Card className="sticky top-24">
              <CardHeader className="pb-3">
                <CardTitle>Navigation</CardTitle>
              </CardHeader>
              <CardContent className="p-0">
                <div className="space-y-1 px-3">
                  <Button
                    variant={activeTab === "dashboard" ? "default" : "ghost"}
                    className="w-full justify-start"
                    onClick={() => setActiveTab("dashboard")}
                  >
                    <BarChart3 className="h-4 w-4 mr-2" />
                    Dashboard
                  </Button>
                  <Button
                    variant={activeTab === "analyze" ? "default" : "ghost"}
                    className="w-full justify-start"
                    onClick={() => setActiveTab("analyze")}
                  >
                    <Search className="h-4 w-4 mr-2" />
                    Analyze Content
                  </Button>
                  {result && (
                    <Button
                      variant={activeTab === "results" ? "default" : "ghost"}
                      className="w-full justify-start"
                      onClick={() => setActiveTab("results")}
                    >
                      <BarChart3 className="h-4 w-4 mr-2" />
                      Results
                    </Button>
                  )}
                </div>
              </CardContent>
              <Separator className="my-3" />
              <CardContent>
                <h3 className="font-medium mb-2 flex items-center">
                  <Lightbulb className="h-4 w-4 mr-2 text-yellow-500" />
                  Fake News Tips
                </h3>
                <ul className="space-y-2 text-sm">
                  {fakeTips.map((tip, index) => (
                    <li key={index} className="flex items-start">
                      <ChevronRight className="h-4 w-4 mr-1 mt-0.5 text-blue-500" />
                      <span>{tip}</span>
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          </div>

          {/* Main Content */}
          <div className="lg:col-span-3 space-y-6">
            {activeTab === "dashboard" && (
              <div className="space-y-6">
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <Card>
                    <CardHeader className="pb-2">
                      <CardTitle className="text-sm font-medium text-muted-foreground">Total Analyses</CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="text-2xl font-bold">{stats.totalAnalyses}</div>
                      <p className="text-xs text-muted-foreground mt-1">+12% from last week</p>
                    </CardContent>
                  </Card>
                  <Card>
                    <CardHeader className="pb-2">
                      <CardTitle className="text-sm font-medium text-muted-foreground">Fake News Detected</CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="text-2xl font-bold">{stats.fakeNewsDetected}</div>
                      <p className="text-xs text-muted-foreground mt-1">
                        {stats.totalAnalyses > 0
                          ? `${((stats.fakeNewsDetected / stats.totalAnalyses) * 100).toFixed(1)}% of total analyses`
                          : "No analyses yet"}
                      </p>
                    </CardContent>
                  </Card>
                  <Card>
                    <CardHeader className="pb-2">
                      <CardTitle className="text-sm font-medium text-muted-foreground">Average Score</CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="text-2xl font-bold">{stats.averageScore.toFixed(1)}%</div>
                      <p className="text-xs text-muted-foreground mt-1">+3.2% from last week</p>
                    </CardContent>
                  </Card>
                </div>

                <Card>
                  <CardHeader>
                    <CardTitle>Recent Analyses</CardTitle>
                    <CardDescription>Your recently analyzed content</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-4">
                      {recentAnalyses.map((item, index) => (
                        <div
                          key={index}
                          className="flex items-center justify-between p-3 rounded-lg border bg-card hover:bg-accent/50 transition-colors"
                        >
                          <div className="flex items-center space-x-3">
                            <div
                              className={`p-2 rounded-full ${getStatusBg(item.score)} ${getStatusBorder(item.score)}`}
                            >
                              {getStatusIcon(item.score)}
                            </div>
                            <div>
                              <h3 className="font-medium">{item.title}</h3>
                              {item.url && (
                                <div className="flex items-center text-sm text-muted-foreground">
                                  <LinkIcon className="h-3 w-3 mr-1" />
                                  <span className="truncate max-w-[200px]">{item.url}</span>
                                </div>
                              )}
                            </div>
                          </div>
                          <div className="flex items-center space-x-4">
                            <div className="text-sm text-muted-foreground">{item.relativeDate}</div>
                            <Badge variant={item.score > 70 ? "success" : item.score > 40 ? "warning" : "destructive"}>
                              {Math.round(item.score)}%
                            </Badge>
                            <Button
                              variant="ghost"
                              size="icon"
                              onClick={() => {
                                setActiveTab("results")
                                setResult({
                                  success: true,
                                  score: item.score,
                                  verdict: item.verdict,
                                  explanation: "This is a previously analyzed article.",
                                  factors: [
                                    { name: "Source Credibility", score: item.score + (Math.random() * 10 - 5) },
                                    { name: "Emotional Language", score: item.score + (Math.random() * 10 - 5) },
                                    { name: "Fact Consistency", score: item.score + (Math.random() * 10 - 5) },
                                    { name: "Clickbait Detection", score: item.score + (Math.random() * 10 - 5) },
                                    { name: "Conspiracy Theories", score: item.score + (Math.random() * 10 - 5) },
                                  ].map((f) => ({
                                    name: f.name,
                                    score: Math.max(0, Math.min(100, Math.round(f.score))),
                                  })),
                                })
                              }}
                            >
                              <ExternalLink className="h-4 w-4" />
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                  <CardFooter>
                    <Button variant="outline" className="w-full" onClick={viewAllAnalyses}>
                      View All Analyses
                    </Button>
                  </CardFooter>
                </Card>

                <Card>
                  <CardHeader>
                    <CardTitle>How It Works</CardTitle>
                    <CardDescription>Understanding our fake news detection process</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                      <div className="flex flex-col items-center text-center p-4 rounded-lg border bg-card">
                        <div className="p-3 rounded-full bg-blue-50 border border-blue-100 mb-3">
                          <Search className="h-6 w-6 text-blue-600" />
                        </div>
                        <h3 className="font-medium mb-2">Content Analysis</h3>
                        <p className="text-sm text-muted-foreground">
                          We analyze the text for emotional language, clickbait tactics, and factual consistency.
                        </p>
                      </div>
                      <div className="flex flex-col items-center text-center p-4 rounded-lg border bg-card">
                        <div className="p-3 rounded-full bg-purple-50 border border-purple-100 mb-3">
                          <BookOpen className="h-6 w-6 text-purple-600" />
                        </div>
                        <h3 className="font-medium mb-2">Source Evaluation</h3>
                        <p className="text-sm text-muted-foreground">
                          We evaluate the credibility of the source based on its history and reputation.
                        </p>
                      </div>
                      <div className="flex flex-col items-center text-center p-4 rounded-lg border bg-card">
                        <div className="p-3 rounded-full bg-green-50 border border-green-100 mb-3">
                          <BarChart3 className="h-6 w-6 text-green-600" />
                        </div>
                        <h3 className="font-medium mb-2">Comprehensive Score</h3>
                        <p className="text-sm text-muted-foreground">
                          We combine multiple factors to generate a comprehensive credibility score.
                        </p>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </div>
            )}

            {activeTab === "analyze" && (
              <Card className="shadow-lg border-t-4 border-t-blue-500">
                <CardHeader>
                  <CardTitle className="text-2xl">Analyze News Content</CardTitle>
                  <CardDescription>Enter a URL or paste text content to analyze its credibility</CardDescription>
                </CardHeader>
                <CardContent>
                  <Tabs defaultValue="url" className="w-full">
                    <TabsList className="grid w-full grid-cols-2 mb-6">
                      <TabsTrigger value="url" className="flex items-center gap-2">
                        <LinkIcon className="h-4 w-4" />
                        Analyze URL
                      </TabsTrigger>
                      <TabsTrigger value="text" className="flex items-center gap-2">
                        <FileText className="h-4 w-4" />
                        Analyze Text
                      </TabsTrigger>
                    </TabsList>

                    <TabsContent value="url">
                      <div className="space-y-4">
                        <div className="space-y-2">
                          <label htmlFor="url-input" className="text-sm font-medium">
                            News Article URL
                          </label>
                          <div className="flex gap-2">
                            <Input
                              id="url-input"
                              placeholder="https://example.com/news-article"
                              value={url}
                              onChange={(e) => setUrl(e.target.value)}
                              className="flex-1"
                            />
                            <Button onClick={() => analyzeNews("url")} disabled={isAnalyzing || !url} className="gap-2">
                              {isAnalyzing ? (
                                <>
                                  <Loader2 className="h-4 w-4 animate-spin" />
                                  Analyzing...
                                </>
                              ) : (
                                <>
                                  <Search className="h-4 w-4" />
                                  Analyze
                                </>
                              )}
                            </Button>
                          </div>
                          <p className="text-xs text-muted-foreground">
                            Enter the full URL of a news article to analyze its credibility
                          </p>
                        </div>

                        <div className="bg-blue-50 dark:bg-blue-950 p-4 rounded-lg border border-blue-100 dark:border-blue-900">
                          <div className="flex items-start">
                            <Info className="h-5 w-5 text-blue-600 dark:text-blue-400 mt-0.5 mr-3 flex-shrink-0" />
                            <div>
                              <h4 className="font-medium text-blue-800 dark:text-blue-300 mb-1">
                                How URL Analysis Works
                              </h4>
                              <p className="text-sm text-blue-700 dark:text-blue-400">
                                When you submit a URL, our system will fetch the content, extract the text, and analyze
                                it for signs of misinformation using our advanced algorithms.
                              </p>
                            </div>
                          </div>
                        </div>
                      </div>
                    </TabsContent>

                    <TabsContent value="text">
                      <div className="space-y-4">
                        <div className="space-y-2">
                          <label htmlFor="text-input" className="text-sm font-medium">
                            News Article Text
                          </label>
                          <Textarea
                            id="text-input"
                            placeholder="Paste news article text here..."
                            className="min-h-[200px] resize-y"
                            value={text}
                            onChange={(e) => setText(e.target.value)}
                          />
                          <p className="text-xs text-muted-foreground">
                            Paste the full text of a news article to analyze its credibility
                          </p>
                        </div>

                        <div className="flex justify-end">
                          <Button onClick={() => analyzeNews("text")} disabled={isAnalyzing || !text} className="gap-2">
                            {isAnalyzing ? (
                              <>
                                <Loader2 className="h-4 w-4 animate-spin" />
                                Analyzing...
                              </>
                            ) : (
                              <>
                                <Search className="h-4 w-4" />
                                Analyze
                              </>
                            )}
                          </Button>
                        </div>

                        <div className="bg-blue-50 dark:bg-blue-950 p-4 rounded-lg border border-blue-100 dark:border-blue-900">
                          <div className="flex items-start">
                            <Info className="h-5 w-5 text-blue-600 dark:text-blue-400 mt-0.5 mr-3 flex-shrink-0" />
                            <div>
                              <h4 className="font-medium text-blue-800 dark:text-blue-300 mb-1">
                                How Text Analysis Works
                              </h4>
                              <p className="text-sm text-blue-700 dark:text-blue-400">
                                Our system analyzes the text for emotional language, clickbait tactics, factual
                                consistency, and other indicators of fake news.
                              </p>
                            </div>
                          </div>
                        </div>
                      </div>
                    </TabsContent>
                  </Tabs>
                </CardContent>
              </Card>
            )}

            {isAnalyzing && (
              <Card className="border-t-4 border-t-blue-500 shadow-lg">
                <CardContent className="pt-6">
                  <div className="flex flex-col items-center justify-center py-8">
                    <div className="relative mb-6">
                      <div className="absolute inset-0 flex items-center justify-center">
                        <Loader2 className="h-12 w-12 text-blue-500 animate-spin" />
                      </div>
                      <Newspaper className="h-16 w-16 text-muted-foreground opacity-20" />
                    </div>
                    <h3 className="text-xl font-medium mb-3">Analyzing content...</h3>
                    <p className="text-sm text-muted-foreground mb-6 text-center max-w-md">
                      Our AI is examining the article for signs of misinformation, checking source credibility,
                      analyzing language patterns, and comparing with known facts.
                    </p>
                    <Progress value={45} className="w-full max-w-md mb-2" />
                    <p className="text-xs text-muted-foreground">This may take a few moments</p>
                  </div>
                </CardContent>
              </Card>
            )}

            {error && (
              <Alert variant="destructive" className="border-t-4 border-t-red-500 shadow-lg">
                <AlertCircle className="h-4 w-4" />
                <AlertTitle>Error</AlertTitle>
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}

            {activeTab === "results" && result && !isAnalyzing && (
              <div className="space-y-6">
                <Card
                  className={`border-t-4 ${
                    result.score > 70
                      ? "border-t-green-500"
                      : result.score > 40
                        ? "border-t-yellow-500"
                        : "border-t-red-500"
                  } shadow-lg`}
                >
                  <CardHeader>
                    <div className="flex items-center justify-between">
                      <CardTitle className="text-2xl">Analysis Results</CardTitle>
                      <div className={`p-2 rounded-full ${getStatusBg(result.score)} ${getStatusBorder(result.score)}`}>
                        {getStatusIcon(result.score)}
                      </div>
                    </div>
                    <CardDescription>Our AI-powered analysis of the provided content</CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-6">
                    <div
                      className={`p-6 rounded-lg ${getStatusBg(result.score)} ${getStatusBorder(result.score)} border`}
                    >
                      <h3 className={`text-xl font-bold mb-2 ${getStatusText(result.score)}`}>{result.verdict}</h3>
                      <p className="text-gray-700 dark:text-gray-300">{result.explanation}</p>
                    </div>

                    <div>
                      <h3 className="text-lg font-medium mb-4 flex items-center">
                        <BarChart3 className="h-5 w-5 mr-2 text-blue-500" />
                        Credibility Score
                      </h3>
                      <div className="bg-gray-100 dark:bg-gray-800 p-6 rounded-lg">
                        <div className="flex items-center gap-4 mb-2">
                          <Progress value={result.score} className="flex-1 h-3" />
                          <span className={`text-2xl font-bold ${getStatusText(result.score)}`}>
                            {Math.round(result.score)}%
                          </span>
                        </div>
                        <div className="flex justify-between text-xs text-muted-foreground mt-1">
                          <span>Likely Fake</span>
                          <span>Questionable</span>
                          <span>Likely Legitimate</span>
                        </div>
                      </div>
                    </div>

                    <div>
                      <h3 className="text-lg font-medium mb-4 flex items-center">
                        <BarChart3 className="h-5 w-5 mr-2 text-blue-500" />
                        Analysis Factors
                      </h3>
                      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                        {result.factors.map((factor, index) => (
                          <Card key={index} className="overflow-hidden">
                            <div className={`h-1 ${getStatusColor(factor.score)}`}></div>
                            <CardContent className="pt-4">
                              <div className="flex justify-between items-center mb-2">
                                <h4 className="font-medium">{factor.name}</h4>
                                <Badge
                                  variant={
                                    factor.score > 70 ? "success" : factor.score > 40 ? "warning" : "destructive"
                                  }
                                >
                                  {Math.round(factor.score)}%
                                </Badge>
                              </div>
                              <Progress value={factor.score} className="h-2" />
                              <p className="text-xs text-muted-foreground mt-2">
                                {factor.score > 70
                                  ? "Strong indicators of credibility"
                                  : factor.score > 40
                                    ? "Some concerns detected"
                                    : "Significant issues detected"}
                              </p>
                            </CardContent>
                          </Card>
                        ))}
                      </div>
                    </div>

                    <div className="bg-blue-50 dark:bg-blue-950 p-4 rounded-lg border border-blue-100 dark:border-blue-900">
                      <div className="flex items-start">
                        <Info className="h-5 w-5 text-blue-600 dark:text-blue-400 mt-0.5 mr-3 flex-shrink-0" />
                        <div>
                          <h4 className="font-medium text-blue-800 dark:text-blue-300 mb-1">What to do next</h4>
                          <p className="text-sm text-blue-700 dark:text-blue-400 mb-2">
                            Based on our analysis, here are some recommended actions:
                          </p>
                          <ul className="text-sm text-blue-700 dark:text-blue-400 space-y-1 list-disc pl-5">
                            <li>Verify this information with other reputable sources</li>
                            <li>Check the publication date to ensure it's current</li>
                            <li>Look for the original source of any claims made</li>
                            <li>Consider the author's expertise and potential biases</li>
                          </ul>
                        </div>
                      </div>
                    </div>
                  </CardContent>
                  <CardFooter className="flex justify-between">
                    <Button variant="outline" onClick={() => setActiveTab("analyze")}>
                      Analyze Another
                    </Button>
                    <div className="flex gap-2">
                      <Button variant="outline" onClick={saveResult}>
                        <Save className="h-4 w-4 mr-2" />
                        Save
                      </Button>
                      <Button variant="outline" onClick={shareResult}>
                        <Share2 className="h-4 w-4 mr-2" />
                        Share
                      </Button>
                      <Button onClick={() => alert("Download functionality would be implemented here")}>
                        <Download className="h-4 w-4 mr-2" />
                        Export
                      </Button>
                    </div>
                  </CardFooter>
                </Card>

                <Card>
                  <CardHeader>
                    <CardTitle>Detailed Explanation</CardTitle>
                    <CardDescription>Understanding the factors in our analysis</CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div className="space-y-2">
                      <h3 className="font-medium">Source Credibility</h3>
                      <p className="text-sm text-muted-foreground">
                        We evaluate the source based on its history of accurate reporting, transparency, and reputation.
                        Sources with established credibility receive higher scores.
                      </p>
                    </div>
                    <div className="space-y-2">
                      <h3 className="font-medium">Emotional Language</h3>
                      <p className="text-sm text-muted-foreground">
                        Objective reporting uses neutral language. We analyze the text for emotionally charged words
                        that may indicate an attempt to manipulate the reader's emotions.
                      </p>
                    </div>
                    <div className="space-y-2">
                      <h3 className="font-medium">Fact Consistency</h3>
                      <p className="text-sm text-muted-foreground">
                        We check for internal consistency and compare claims against known facts. Articles with
                        verifiable information score higher.
                      </p>
                    </div>
                    <div className="space-y-2">
                      <h3 className="font-medium">Clickbait Detection</h3>
                      <p className="text-sm text-muted-foreground">
                        Clickbait headlines often use sensationalist language to attract attention. We identify common
                        clickbait patterns and tactics.
                      </p>
                    </div>
                    <div className="space-y-2">
                      <h3 className="font-medium">Conspiracy Theories</h3>
                      <p className="text-sm text-muted-foreground">
                        We detect language patterns common in conspiracy theories, such as claims of cover-ups or secret
                        agendas without substantial evidence.
                      </p>
                    </div>
                  </CardContent>
                </Card>
              </div>
            )}
          </div>
        </div>
      </main>

      <footer className="bg-white dark:bg-gray-950 border-t border-gray-200 dark:border-gray-800 py-6">
        <div className="container mx-auto px-4">
          <div className="flex flex-col md:flex-row justify-between items-center">
            <div className="flex items-center space-x-2 mb-4 md:mb-0">
              <Shield className="h-5 w-5 text-blue-600 dark:text-blue-400" />
              <span className="font-bold text-gray-900 dark:text-white">Fake News Detector</span>
            </div>
            <div className="text-sm text-gray-500 dark:text-gray-400">
              © {new Date().getFullYear()} Fake News Detector. All rights reserved.
            </div>
            <div className="flex space-x-4 mt-4 md:mt-0">
              <Button variant="ghost" size="sm" onClick={() => alert("Privacy Policy would be shown here")}>
                Privacy Policy
              </Button>
              <Button variant="ghost" size="sm" onClick={() => alert("Terms of Service would be shown here")}>
                Terms of Service
              </Button>
              <Button variant="ghost" size="sm" onClick={() => alert("Contact form would be shown here")}>
                Contact
              </Button>
            </div>
          </div>
        </div>
      </footer>

      {/* Save confirmation dialog */}
      {showSaveDialog && (
        <div className="fixed bottom-4 right-4 bg-green-100 border border-green-200 text-green-800 rounded-lg p-4 shadow-lg dark:bg-green-900 dark:border-green-800 dark:text-green-200 animate-in slide-in-from-bottom-5">
          <div className="flex items-center">
            <CheckCircle className="h-5 w-5 mr-2" />
            <p>Analysis saved successfully!</p>
          </div>
        </div>
      )}

      {/* Share dialog */}
      {showShareDialog && (
        <div className="fixed bottom-4 right-4 bg-blue-100 border border-blue-200 text-blue-800 rounded-lg p-4 shadow-lg dark:bg-blue-900 dark:border-blue-800 dark:text-blue-200 animate-in slide-in-from-bottom-5">
          <div className="flex items-center">
            <Share2 className="h-5 w-5 mr-2" />
            <p>Analysis shared successfully!</p>
          </div>
        </div>
      )}

      {/* About dialog */}
      {showAboutDialog && (
        <div
          className="fixed inset-0 bg-black/50 flex items-center justify-center z-50"
          onClick={() => setShowAboutDialog(false)}
        >
          <div className="bg-white dark:bg-gray-900 rounded-lg p-6 max-w-md mx-4" onClick={(e) => e.stopPropagation()}>
            <h2 className="text-xl font-bold mb-4 flex items-center">
              <Shield className="h-5 w-5 mr-2 text-blue-600" />
              About Fake News Detector
            </h2>
            <p className="mb-4 text-gray-700 dark:text-gray-300">
              Fake News Detector is an AI-powered tool that helps you identify potentially misleading or fake news
              articles. Our system analyzes content for various indicators of credibility and provides you with a
              comprehensive assessment.
            </p>
            <p className="mb-4 text-gray-700 dark:text-gray-300">
              This tool is designed for educational purposes and should be used as one of many resources to evaluate
              information critically.
            </p>
            <div className="flex justify-end">
              <Button onClick={() => setShowAboutDialog(false)}>Close</Button>
            </div>
          </div>
        </div>
      )}

      {/* Debug dialog */}
      {showDebugDialog && (
        <div
          className="fixed inset-0 bg-black/50 flex items-center justify-center z-50"
          onClick={() => setShowDebugDialog(false)}
        >
          <div
            className="bg-white dark:bg-gray-900 rounded-lg p-6 max-w-3xl mx-4 max-h-[80vh] overflow-auto"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 className="text-xl font-bold mb-4 flex items-center">
              <Bug className="h-5 w-5 mr-2 text-red-600" />
              Debug Information
            </h2>

            <div className="mb-4">
              <h3 className="font-medium mb-2">Troubleshooting Steps</h3>
              <ol className="list-decimal pl-5 space-y-2 text-sm">
                <li>
                  Make sure the .NET backend is running. Open a terminal and run{" "}
                  <code className="bg-gray-100 dark:bg-gray-800 px-1 py-0.5 rounded">dotnet run</code> in the project
                  root.
                </li>
                <li>
                  Check if the backend is accessible at{" "}
                  <code className="bg-gray-100 dark:bg-gray-800 px-1 py-0.5 rounded">http://localhost:5000</code> or{" "}
                  <code className="bg-gray-100 dark:bg-gray-800 px-1 py-0.5 rounded">https://localhost:7126</code>.
                </li>
                <li>Verify that CORS is properly configured in the backend.</li>
                <li>Check if the API endpoints are correctly defined in the backend.</li>
                <li>Ensure there are no port conflicts with other applications.</li>
              </ol>
            </div>

            <div className="mb-4">
              <h3 className="font-medium mb-2">API Errors</h3>
              <div className="bg-gray-100 dark:bg-gray-800 p-4 rounded-lg text-xs font-mono overflow-auto max-h-60">
                <h4 className="font-bold mb-2">Recent Analyses Errors:</h4>
                <pre>{JSON.stringify(debugInfo.recentAnalysesErrors || [], null, 2)}</pre>

                <h4 className="font-bold mt-4 mb-2">Stats Errors:</h4>
                <pre>{JSON.stringify(debugInfo.statsErrors || [], null, 2)}</pre>

                <h4 className="font-bold mt-4 mb-2">Analyze Errors:</h4>
                <pre>{JSON.stringify(debugInfo.analyzeErrors || [], null, 2)}</pre>
              </div>
            </div>

            <div className="mb-4">
              <h3 className="font-medium mb-2">Running the Backend</h3>
              <p className="text-sm mb-2">Make sure you're running the backend with:</p>
              <div className="bg-gray-100 dark:bg-gray-800 p-2 rounded-lg text-xs font-mono">
                <code>dotnet run</code>
              </div>
              <p className="text-sm mt-2">
                Or use Visual Studio Code's Run and Debug feature with the ".NET Core Launch (web)" configuration.
              </p>
            </div>

            <div className="flex justify-end">
              <Button onClick={() => setShowDebugDialog(false)}>Close</Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
